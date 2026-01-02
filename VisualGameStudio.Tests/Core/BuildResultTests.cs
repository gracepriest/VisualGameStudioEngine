using NUnit.Framework;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Tests.Core;

[TestFixture]
public class BuildResultTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var result = new BuildResult();

        Assert.That(result.Success, Is.False);
        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.OutputPath, Is.Null);
        Assert.That(result.ExecutablePath, Is.Null);
        Assert.That(result.GeneratedCode, Is.Null);
        Assert.That(result.GeneratedFileName, Is.Null);
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.Duration, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Success_CanBeSetAndRetrieved()
    {
        var result = new BuildResult { Success = true };

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public void ExitCode_CanBeSetAndRetrieved()
    {
        var result = new BuildResult { ExitCode = 1 };

        Assert.That(result.ExitCode, Is.EqualTo(1));
    }

    [Test]
    public void OutputPath_CanBeSetAndRetrieved()
    {
        var result = new BuildResult { OutputPath = @"C:\Build\Output" };

        Assert.That(result.OutputPath, Is.EqualTo(@"C:\Build\Output"));
    }

    [Test]
    public void ExecutablePath_CanBeSetAndRetrieved()
    {
        var result = new BuildResult { ExecutablePath = @"C:\Build\Output\App.exe" };

        Assert.That(result.ExecutablePath, Is.EqualTo(@"C:\Build\Output\App.exe"));
    }

    [Test]
    public void GeneratedCode_CanBeSetAndRetrieved()
    {
        var code = "public class Program { }";
        var result = new BuildResult { GeneratedCode = code };

        Assert.That(result.GeneratedCode, Is.EqualTo(code));
    }

    [Test]
    public void GeneratedFileName_CanBeSetAndRetrieved()
    {
        var result = new BuildResult { GeneratedFileName = "Program.cs" };

        Assert.That(result.GeneratedFileName, Is.EqualTo("Program.cs"));
    }

    [Test]
    public void Duration_CanBeSetAndRetrieved()
    {
        var duration = TimeSpan.FromSeconds(5.5);
        var result = new BuildResult { Duration = duration };

        Assert.That(result.Duration, Is.EqualTo(duration));
    }

    [Test]
    public void Diagnostics_CanAddItems()
    {
        var result = new BuildResult();
        result.Diagnostics.Add(new DiagnosticItem { Id = "BC1001", Message = "Error 1" });
        result.Diagnostics.Add(new DiagnosticItem { Id = "BC1002", Message = "Error 2" });

        Assert.That(result.Diagnostics, Has.Count.EqualTo(2));
    }

    [Test]
    public void ErrorCount_WithNoErrors_ReturnsZero()
    {
        var result = new BuildResult();

        Assert.That(result.ErrorCount, Is.EqualTo(0));
    }

    [Test]
    public void ErrorCount_WithErrors_ReturnsCorrectCount()
    {
        var result = new BuildResult();
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Error });
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Error });
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Warning });

        Assert.That(result.ErrorCount, Is.EqualTo(2));
    }

    [Test]
    public void WarningCount_WithNoWarnings_ReturnsZero()
    {
        var result = new BuildResult();

        Assert.That(result.WarningCount, Is.EqualTo(0));
    }

    [Test]
    public void WarningCount_WithWarnings_ReturnsCorrectCount()
    {
        var result = new BuildResult();
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Error });
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Warning });
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Warning });
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Warning });

        Assert.That(result.WarningCount, Is.EqualTo(3));
    }

    [Test]
    public void Errors_WithNoErrors_ReturnsEmpty()
    {
        var result = new BuildResult();
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Warning });
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Info });

        Assert.That(result.Errors, Is.Empty);
    }

    [Test]
    public void Errors_ReturnsOnlyErrors()
    {
        var result = new BuildResult();
        result.Diagnostics.Add(new DiagnosticItem { Id = "E1", Severity = DiagnosticSeverity.Error });
        result.Diagnostics.Add(new DiagnosticItem { Id = "W1", Severity = DiagnosticSeverity.Warning });
        result.Diagnostics.Add(new DiagnosticItem { Id = "E2", Severity = DiagnosticSeverity.Error });

        var errors = result.Errors.ToList();

        Assert.That(errors, Has.Count.EqualTo(2));
        Assert.That(errors.All(e => e.Severity == DiagnosticSeverity.Error), Is.True);
    }

    [Test]
    public void Warnings_WithNoWarnings_ReturnsEmpty()
    {
        var result = new BuildResult();
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Error });
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Info });

        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public void Warnings_ReturnsOnlyWarnings()
    {
        var result = new BuildResult();
        result.Diagnostics.Add(new DiagnosticItem { Id = "E1", Severity = DiagnosticSeverity.Error });
        result.Diagnostics.Add(new DiagnosticItem { Id = "W1", Severity = DiagnosticSeverity.Warning });
        result.Diagnostics.Add(new DiagnosticItem { Id = "W2", Severity = DiagnosticSeverity.Warning });

        var warnings = result.Warnings.ToList();

        Assert.That(warnings, Has.Count.EqualTo(2));
        Assert.That(warnings.All(w => w.Severity == DiagnosticSeverity.Warning), Is.True);
    }

    [Test]
    public void Errors_PreservesOrder()
    {
        var result = new BuildResult();
        result.Diagnostics.Add(new DiagnosticItem { Id = "E1", Severity = DiagnosticSeverity.Error });
        result.Diagnostics.Add(new DiagnosticItem { Id = "W1", Severity = DiagnosticSeverity.Warning });
        result.Diagnostics.Add(new DiagnosticItem { Id = "E2", Severity = DiagnosticSeverity.Error });
        result.Diagnostics.Add(new DiagnosticItem { Id = "E3", Severity = DiagnosticSeverity.Error });

        var errors = result.Errors.ToList();

        Assert.That(errors[0].Id, Is.EqualTo("E1"));
        Assert.That(errors[1].Id, Is.EqualTo("E2"));
        Assert.That(errors[2].Id, Is.EqualTo("E3"));
    }

    [Test]
    public void Warnings_PreservesOrder()
    {
        var result = new BuildResult();
        result.Diagnostics.Add(new DiagnosticItem { Id = "W1", Severity = DiagnosticSeverity.Warning });
        result.Diagnostics.Add(new DiagnosticItem { Id = "E1", Severity = DiagnosticSeverity.Error });
        result.Diagnostics.Add(new DiagnosticItem { Id = "W2", Severity = DiagnosticSeverity.Warning });
        result.Diagnostics.Add(new DiagnosticItem { Id = "W3", Severity = DiagnosticSeverity.Warning });

        var warnings = result.Warnings.ToList();

        Assert.That(warnings[0].Id, Is.EqualTo("W1"));
        Assert.That(warnings[1].Id, Is.EqualTo("W2"));
        Assert.That(warnings[2].Id, Is.EqualTo("W3"));
    }

    [Test]
    public void SuccessfulBuild_HasExpectedProperties()
    {
        var result = new BuildResult
        {
            Success = true,
            ExitCode = 0,
            OutputPath = @"C:\Build\bin\Debug",
            ExecutablePath = @"C:\Build\bin\Debug\App.exe",
            Duration = TimeSpan.FromMilliseconds(1500)
        };

        Assert.That(result.Success, Is.True);
        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.ErrorCount, Is.EqualTo(0));
    }

    [Test]
    public void FailedBuild_HasExpectedProperties()
    {
        var result = new BuildResult
        {
            Success = false,
            ExitCode = 1,
            Duration = TimeSpan.FromMilliseconds(500)
        };
        result.Diagnostics.Add(new DiagnosticItem
        {
            Id = "BC1001",
            Message = "Syntax error",
            Severity = DiagnosticSeverity.Error,
            FilePath = @"C:\Project\Main.bas",
            Line = 10,
            Column = 5
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.ErrorCount, Is.EqualTo(1));
    }

    [Test]
    public void DiagnosticsIncludesInfoAndHidden()
    {
        var result = new BuildResult();
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Error });
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Warning });
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Info });
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Hidden });

        Assert.That(result.Diagnostics, Has.Count.EqualTo(4));
        Assert.That(result.ErrorCount, Is.EqualTo(1));
        Assert.That(result.WarningCount, Is.EqualTo(1));
    }

    [Test]
    public void Diagnostics_IsNotNullByDefault()
    {
        var result = new BuildResult();

        Assert.That(result.Diagnostics, Is.Not.Null);
    }

    [Test]
    public void Diagnostics_CanBeReplaced()
    {
        var result = new BuildResult();
        var newList = new List<DiagnosticItem>
        {
            new() { Id = "BC1001", Message = "Error 1" },
            new() { Id = "BC1002", Message = "Error 2" }
        };

        result.Diagnostics = newList;

        Assert.That(result.Diagnostics, Is.SameAs(newList));
        Assert.That(result.Diagnostics, Has.Count.EqualTo(2));
    }
}
