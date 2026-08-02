using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Analyzer warnings are non-fatal. Historically <c>SemanticAnalyzer.Analyze</c>
/// returned <c>_errors.Count == 0</c> and <c>CompilationResult.HasErrors</c> was
/// <c>AllErrors.Count &gt; 0</c> — neither filtered by severity, so every
/// <c>Warning(...)</c> call site (e.g. "Condition should be Boolean") failed the
/// whole build. These tests pin the corrected contract:
/// warnings-only → success, warnings still reported with Warning severity;
/// warning + error → failure with both present.
/// </summary>
[TestFixture]
public class NonFatalWarningTests
{
    // Triggers exactly one analyzer warning (IfStatementNode: "Condition should
    // be Boolean, got 'Integer'") and nothing else.
    private const string WarningOnlySource = @"Module Program
    Sub Main()
        Dim x As Integer
        If 1 Then
            x = 2
        End If
        PrintLine(x)
    End Sub
End Module
";

    // Same warning, plus a genuine Error-severity failure (undefined identifier).
    private const string WarningPlusErrorSource = @"Module Program
    Sub Main()
        Dim x As Integer
        If 1 Then
            x = undefinedVar
        End If
        PrintLine(x)
    End Sub
End Module
";

    private static ProgramNode ParseProgram(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "test program must parse cleanly: " + string.Join("; ", parser.Errors));
        return ast;
    }

    private static string WriteTempProgram(string source, out string tempDir)
    {
        tempDir = Path.Combine(Path.GetTempPath(),
            "BasicLang_NonFatalWarnTests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var basFile = Path.Combine(tempDir, "Program.bas");
        File.WriteAllText(basFile, source);
        return basFile;
    }

    // ------------------------------------------------------------------
    // 1. Analyzer level
    // ------------------------------------------------------------------

    [Test]
    public void Analyze_WarningOnly_ReturnsTrue_AndKeepsWarningInErrors()
    {
        var analyzer = new SemanticAnalyzer();
        var success = analyzer.Analyze(ParseProgram(WarningOnlySource));

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True,
                "warnings alone must not fail analysis: "
                + string.Join("; ", analyzer.Errors));
            Assert.That(analyzer.Errors, Has.Count.EqualTo(1),
                "expected exactly the one If-condition warning: "
                + string.Join("; ", analyzer.Errors));
            Assert.That(analyzer.Errors[0].Severity, Is.EqualTo(ErrorSeverity.Warning));
            Assert.That(analyzer.Errors[0].Message, Does.Contain("Condition should be Boolean"));
        });
    }

    [Test]
    public void Analyze_WarningPlusError_ReturnsFalse()
    {
        var analyzer = new SemanticAnalyzer();
        var success = analyzer.Analyze(ParseProgram(WarningPlusErrorSource));

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False, "an Error-severity entry must still fail analysis");
            Assert.That(analyzer.Errors.Where(e => e.Severity == ErrorSeverity.Error), Is.Not.Empty);
            Assert.That(analyzer.Errors.Where(e => e.Severity == ErrorSeverity.Warning), Is.Not.Empty,
                "the warning must not be dropped just because an error is present");
        });
    }

    // ------------------------------------------------------------------
    // 2. Compiler level (real CompileFile path, C# backend)
    // ------------------------------------------------------------------

    [Test]
    public void CompileFile_WarningOnly_SucceedsWithWarningSeverityPreserved()
    {
        var basFile = WriteTempProgram(WarningOnlySource, out var tempDir);
        try
        {
            var result = new BasicCompiler().CompileFile(basFile);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True,
                    "warnings-only compile must succeed: "
                    + string.Join("; ", result.AllErrors.Select(e => e.Message)));
                Assert.That(result.HasErrors, Is.False,
                    "HasErrors must count only Error-severity entries");
                Assert.That(result.CombinedIR, Is.Not.Null,
                    "a successful compile must still produce IR");
                Assert.That(result.CombinedIR!.Functions.Any(f => f.Name == "Main"), Is.True,
                    "the unit must actually have been compiled — a warnings-only "
                    + "Analyze() must return true so IR generation runs (an empty "
                    + "combined module means the unit was treated as failed)");

                var warnings = result.AllErrors
                    .Where(e => e.Severity == ErrorSeverity.Warning).ToList();
                Assert.That(warnings, Has.Count.EqualTo(1),
                    "the warning must remain in AllErrors, with severity preserved "
                    + "end-to-end (FinalizeResult/WithInlineLocation copies must carry "
                    + "Severity): " + string.Join("; ", result.AllErrors.Select(
                        e => $"{e.Severity}: {e.Message}")));
                Assert.That(warnings[0].Message, Does.Contain("Condition should be Boolean"));
            });
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Test]
    public void CompileFile_WarningPlusError_FailsWithBothPresent()
    {
        var basFile = WriteTempProgram(WarningPlusErrorSource, out var tempDir);
        try
        {
            var result = new BasicCompiler().CompileFile(basFile);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False,
                    "a real error must still fail the build even alongside warnings");
                Assert.That(result.HasErrors, Is.True);
                Assert.That(
                    result.AllErrors.Where(e => e.Severity == ErrorSeverity.Error
                        && e.Message.Contains("undefinedVar")),
                    Is.Not.Empty, "the undefined-identifier error must be present");
                Assert.That(
                    result.AllErrors.Where(e => e.Severity == ErrorSeverity.Warning
                        && e.Message.Contains("Condition should be Boolean")),
                    Is.Not.Empty, "the warning must render alongside the error");
            });
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    // ------------------------------------------------------------------
    // 3. Rendering: warnings render as warnings, once
    // ------------------------------------------------------------------

    [Test]
    public void CompileFile_WarningOnly_MessageRendersAsWarning_NotError()
    {
        var basFile = WriteTempProgram(WarningOnlySource, out var tempDir);
        try
        {
            var result = new BasicCompiler().CompileFile(basFile);

            Assert.That(result.Success, Is.True,
                string.Join("; ", result.AllErrors.Select(e => e.Message)));

            // Build mode / the CLI print only Message per diagnostic, so the
            // rendered text is exactly the AllErrors messages.
            var rendered = string.Join("\n", result.AllErrors.Select(e => e.Message));
            Assert.Multiple(() =>
            {
                Assert.That(rendered, Does.Contain("Warning at line"),
                    "the warning must self-describe as a Warning with its location");
                Assert.That(rendered, Does.Not.Contain("Error at line"),
                    "a warnings-only compile must not render anything as an Error "
                    + "(no doubled 'Error at line N: Warning at line N' prefix)");
                Assert.That(System.Text.RegularExpressions.Regex
                        .Matches(rendered, "Warning at line").Count, Is.EqualTo(1),
                    "the warning must render exactly once");
            });
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    // ------------------------------------------------------------------
    // 4. CLI end-to-end: exit code 0 for warnings-only, warning printed once
    // ------------------------------------------------------------------

    [Test]
    [Category("Integration")]
    public async Task Cli_WarningOnlyCompile_ExitsZero_PrintsWarningOnce()
    {
        var basFile = WriteTempProgram(WarningOnlySource, out var tempDir);
        try
        {
            var (exitCode, stdOut, stdErr) =
                await CliTestHarness.RunCli(tempDir, basFile, "--target=csharp");
            var output = stdOut + "\n" + stdErr;

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(0),
                    $"warnings-only compile must exit 0.\nSTDOUT:\n{stdOut}\nSTDERR:\n{stdErr}");
                Assert.That(System.Text.RegularExpressions.Regex
                        .Matches(output, "Warning at line").Count, Is.EqualTo(1),
                    $"the warning must print exactly once.\nSTDOUT:\n{stdOut}\nSTDERR:\n{stdErr}");
                Assert.That(output, Does.Not.Contain("Error at line"),
                    $"nothing may render as an Error.\nSTDOUT:\n{stdOut}\nSTDERR:\n{stdErr}");
            });
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
