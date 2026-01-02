using NUnit.Framework;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Tests.Core;

[TestFixture]
public class DiagnosticItemTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var diagnostic = new DiagnosticItem();

        Assert.That(diagnostic.Id, Is.EqualTo(""));
        Assert.That(diagnostic.Message, Is.EqualTo(""));
        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(diagnostic.FilePath, Is.Null);
        Assert.That(diagnostic.Line, Is.EqualTo(0));
        Assert.That(diagnostic.Column, Is.EqualTo(0));
        Assert.That(diagnostic.EndLine, Is.EqualTo(0));
        Assert.That(diagnostic.EndColumn, Is.EqualTo(0));
        Assert.That(diagnostic.Source, Is.Null);
    }

    [Test]
    public void Id_CanBeSetAndRetrieved()
    {
        var diagnostic = new DiagnosticItem { Id = "BC1001" };

        Assert.That(diagnostic.Id, Is.EqualTo("BC1001"));
    }

    [Test]
    public void Message_CanBeSetAndRetrieved()
    {
        var diagnostic = new DiagnosticItem { Message = "Variable not declared" };

        Assert.That(diagnostic.Message, Is.EqualTo("Variable not declared"));
    }

    [Test]
    public void Severity_CanBeSetToError()
    {
        var diagnostic = new DiagnosticItem { Severity = DiagnosticSeverity.Error };

        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
    }

    [Test]
    public void Severity_CanBeSetToWarning()
    {
        var diagnostic = new DiagnosticItem { Severity = DiagnosticSeverity.Warning };

        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
    }

    [Test]
    public void Severity_CanBeSetToInfo()
    {
        var diagnostic = new DiagnosticItem { Severity = DiagnosticSeverity.Info };

        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Info));
    }

    [Test]
    public void Severity_CanBeSetToHidden()
    {
        var diagnostic = new DiagnosticItem { Severity = DiagnosticSeverity.Hidden };

        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Hidden));
    }

    [Test]
    public void FilePath_CanBeSetAndRetrieved()
    {
        var diagnostic = new DiagnosticItem { FilePath = @"C:\Project\Source.bas" };

        Assert.That(diagnostic.FilePath, Is.EqualTo(@"C:\Project\Source.bas"));
    }

    [Test]
    public void Line_CanBeSetAndRetrieved()
    {
        var diagnostic = new DiagnosticItem { Line = 42 };

        Assert.That(diagnostic.Line, Is.EqualTo(42));
    }

    [Test]
    public void Column_CanBeSetAndRetrieved()
    {
        var diagnostic = new DiagnosticItem { Column = 15 };

        Assert.That(diagnostic.Column, Is.EqualTo(15));
    }

    [Test]
    public void EndLine_CanBeSetAndRetrieved()
    {
        var diagnostic = new DiagnosticItem { EndLine = 45 };

        Assert.That(diagnostic.EndLine, Is.EqualTo(45));
    }

    [Test]
    public void EndColumn_CanBeSetAndRetrieved()
    {
        var diagnostic = new DiagnosticItem { EndColumn = 20 };

        Assert.That(diagnostic.EndColumn, Is.EqualTo(20));
    }

    [Test]
    public void Source_CanBeSetAndRetrieved()
    {
        var diagnostic = new DiagnosticItem { Source = "BasicLang Compiler" };

        Assert.That(diagnostic.Source, Is.EqualTo("BasicLang Compiler"));
    }

    [Test]
    public void Location_WithFilePath_ReturnsFormattedLocation()
    {
        var diagnostic = new DiagnosticItem
        {
            FilePath = @"C:\Project\Source.bas",
            Line = 42,
            Column = 15
        };

        Assert.That(diagnostic.Location, Is.EqualTo("Source.bas(42,15)"));
    }

    [Test]
    public void Location_WithoutFilePath_ReturnsEmptyString()
    {
        var diagnostic = new DiagnosticItem
        {
            Line = 42,
            Column = 15
        };

        Assert.That(diagnostic.Location, Is.EqualTo(""));
    }

    [Test]
    public void Location_ExtractsFileNameOnly()
    {
        var diagnostic = new DiagnosticItem
        {
            FilePath = @"C:\Very\Long\Path\To\MyFile.bas",
            Line = 10,
            Column = 5
        };

        Assert.That(diagnostic.Location, Is.EqualTo("MyFile.bas(10,5)"));
    }

    [Test]
    public void ToString_ReturnsFormattedString()
    {
        var diagnostic = new DiagnosticItem
        {
            Id = "BC1001",
            Message = "Variable not declared",
            Severity = DiagnosticSeverity.Error,
            FilePath = @"C:\Project\Source.bas",
            Line = 42,
            Column = 15
        };

        var result = diagnostic.ToString();

        Assert.That(result, Does.Contain("Error"));
        Assert.That(result, Does.Contain("BC1001"));
        Assert.That(result, Does.Contain("Variable not declared"));
        Assert.That(result, Does.Contain("Source.bas(42,15)"));
    }

    [Test]
    public void ToString_WithWarning_ContainsWarning()
    {
        var diagnostic = new DiagnosticItem
        {
            Id = "BC2001",
            Message = "Unused variable",
            Severity = DiagnosticSeverity.Warning,
            FilePath = @"C:\Project\Source.bas",
            Line = 10,
            Column = 5
        };

        var result = diagnostic.ToString();

        Assert.That(result, Does.Contain("Warning"));
    }

    [Test]
    public void ToString_WithInfo_ContainsInfo()
    {
        var diagnostic = new DiagnosticItem
        {
            Id = "BC3001",
            Message = "Consider using const",
            Severity = DiagnosticSeverity.Info
        };

        var result = diagnostic.ToString();

        Assert.That(result, Does.Contain("Info"));
    }

    [Test]
    public void ToString_WithHidden_ContainsHidden()
    {
        var diagnostic = new DiagnosticItem
        {
            Id = "BC4001",
            Message = "Hidden diagnostic",
            Severity = DiagnosticSeverity.Hidden
        };

        var result = diagnostic.ToString();

        Assert.That(result, Does.Contain("Hidden"));
    }

    [Test]
    public void AllProperties_CanBeSetTogether()
    {
        var diagnostic = new DiagnosticItem
        {
            Id = "BC1001",
            Message = "Test message",
            Severity = DiagnosticSeverity.Error,
            FilePath = @"C:\Test\File.bas",
            Line = 1,
            Column = 2,
            EndLine = 3,
            EndColumn = 4,
            Source = "Test Source"
        };

        Assert.That(diagnostic.Id, Is.EqualTo("BC1001"));
        Assert.That(diagnostic.Message, Is.EqualTo("Test message"));
        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(diagnostic.FilePath, Is.EqualTo(@"C:\Test\File.bas"));
        Assert.That(diagnostic.Line, Is.EqualTo(1));
        Assert.That(diagnostic.Column, Is.EqualTo(2));
        Assert.That(diagnostic.EndLine, Is.EqualTo(3));
        Assert.That(diagnostic.EndColumn, Is.EqualTo(4));
        Assert.That(diagnostic.Source, Is.EqualTo("Test Source"));
    }
}

[TestFixture]
public class DiagnosticSeverityTests
{
    [Test]
    public void DiagnosticSeverity_Hidden_HasValue0()
    {
        Assert.That((int)DiagnosticSeverity.Hidden, Is.EqualTo(0));
    }

    [Test]
    public void DiagnosticSeverity_Info_HasValue1()
    {
        Assert.That((int)DiagnosticSeverity.Info, Is.EqualTo(1));
    }

    [Test]
    public void DiagnosticSeverity_Warning_HasValue2()
    {
        Assert.That((int)DiagnosticSeverity.Warning, Is.EqualTo(2));
    }

    [Test]
    public void DiagnosticSeverity_Error_HasValue3()
    {
        Assert.That((int)DiagnosticSeverity.Error, Is.EqualTo(3));
    }

    [Test]
    public void DiagnosticSeverity_HasFourValues()
    {
        var values = Enum.GetValues<DiagnosticSeverity>();
        Assert.That(values, Has.Length.EqualTo(4));
    }

    [Test]
    public void DiagnosticSeverity_CanCompare()
    {
        Assert.That(DiagnosticSeverity.Error, Is.GreaterThan(DiagnosticSeverity.Warning));
        Assert.That(DiagnosticSeverity.Warning, Is.GreaterThan(DiagnosticSeverity.Info));
        Assert.That(DiagnosticSeverity.Info, Is.GreaterThan(DiagnosticSeverity.Hidden));
    }
}
