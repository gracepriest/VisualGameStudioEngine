using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 6: <c>DesignDiagnostic</c>, the <c>BL8xxx</c> band, and <c>basiclang design --check</c>.
///
/// <para>The CLI tests are <c>[Category("Integration")]</c> and spawn the REAL compiler through
/// <see cref="CliTestHarness.CliPath"/>, which hard-fails when the binary is missing. ⛔ Not
/// <c>TemplateBuildSweepTests.FindCompiler()</c>, which reads a different binary and degrades to
/// <c>Assert.Inconclusive</c> — a result that reads as "not failed" in a summary and would let this
/// verb rot undetected.</para>
/// </summary>
[TestFixture]
public class DesignCheckCliTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-designcheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    private string Write(string name, string source)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, source);
        return path;
    }

    private const string CleanForm = """
        Public Class MainForm
            Inherits Form

            Private btnClick As Button

            Public Sub New()
                btnClick = New Button()
                btnClick.Text = "Click Me"
                Me.Controls.Add(btnClick)
            End Sub
        End Class
        """;

    private const string FormWithHandles = """
        Public Class MainForm
            Inherits Form
            Private btnClick As Button
            Private Sub btnClick_Click(sender As Object, e As EventArgs) Handles btnClick.Click
            End Sub
        End Class
        """;

    // ==================================================================
    // The carrier
    // ==================================================================

    [Test]
    public void Format_UsesTheClickToNavigateShape_NotTheLabelShape()
    {
        var diagnostic = new DesignDiagnostic("BL8001", "BL8001: nope", "C:\\p\\MainForm.bas", 4, 62, false);

        Assert.That(diagnostic.Format(), Is.EqualTo("C:\\p\\MainForm.bas(4,62): error BL8001: BL8001: nope"),
            "the rendered shape must match CppDiagnostic.FormatNormalized character for character — " +
            "that is the format the IDE Output panel's click-to-navigate regex matches. The CLI's " +
            "other shape, '{label}: {Code}: {Message}', carries no file, line or column.");
    }

    [Test]
    public void Format_DegradesGracefully_WithoutAColumnOrLine()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new DesignDiagnostic("BL8004", "m", "a.bas", 7, 0, true).Format(),
                Is.EqualTo("a.bas(7): warning BL8004: m"));
            Assert.That(new DesignDiagnostic("BL8004", "m", "a.bas", 0, 0, true).Format(),
                Is.EqualTo("a.bas: warning BL8004: m"));
        });
    }

    [Test]
    public void EveryFinding_CarriesItsCodeInTheMessageToo()
    {
        // Two of the CLI's print sites emit only {label}: {Message} and drop the code field, so a
        // code carried only in the field is invisible on those routes.
        var path = Write("Handles.bas", FormWithHandles);

        foreach (var finding in DesignCheck.CheckFile(path))
        {
            Assert.That(finding.Message, Does.StartWith(finding.Code),
                $"'{finding.Code}' must appear in the message string as well as the field");
        }
    }

    // ==================================================================
    // The check
    // ==================================================================

    [Test]
    public void Check_IsClean_ForAWellFormedForm()
    {
        Assert.That(DesignCheck.CheckSource("MainForm.bas", CleanForm), Is.Empty);
    }

    [Test]
    public void Check_ReportsAHandlesClause_AsAnError()
    {
        var findings = DesignCheck.CheckSource("MainForm.bas", FormWithHandles);

        var error = findings.Single(f => f.Code == DesignCodes.HandlesClause);
        Assert.Multiple(() =>
        {
            Assert.That(error.IsWarning, Is.False, "a file that cannot build is not a warning");
            Assert.That(error.Line, Is.EqualTo(4));
            Assert.That(error.Message, Does.Contain("AddHandler"), "a refusal must say what to do instead");
        });
    }

    [Test]
    public void Check_StopsAfterARefusal_RatherThanBuryingItInWarnings()
    {
        var findings = DesignCheck.CheckSource("MainForm.bas", FormWithHandles);

        Assert.That(findings.Select(f => f.Code), Is.EqualTo(new[] { DesignCodes.HandlesClause }),
            "once a file is refused, reporting orphans and unsupported types in a form that cannot " +
            "be imported buries the reason it cannot be");
    }

    [Test]
    public void Check_WarnsAboutAControlThatIsNeverAddedToTheForm()
    {
        var findings = DesignCheck.CheckSource("MainForm.bas", """
            Public Class MainForm
                Inherits Form
                Private btnClick As Button
                Public Sub New()
                    btnClick = New Button()
                End Sub
            End Class
            """);

        var orphan = findings.Single(f => f.Code == DesignCodes.OrphanedControl);
        Assert.Multiple(() =>
        {
            Assert.That(orphan.IsWarning, Is.True);
            Assert.That(orphan.Line, Is.EqualTo(5));
            Assert.That(orphan.Message, Does.Contain("btnClick"));
        });
    }

    [Test]
    public void Check_WarnsAboutAControlTypeTheCatalogDoesNotCarry()
    {
        var findings = DesignCheck.CheckSource("Page.bas", """
            Sub Main()
                Dim doc As Document = ::document
                Dim heading As Element = doc.createElement("h1")
                doc.body.appendChild(heading)
            End Sub
            """);

        var unsupported = findings.Single(f => f.Code == DesignCodes.UnsupportedControl);
        Assert.Multiple(() =>
        {
            Assert.That(unsupported.IsWarning, Is.True,
                "an unsupported type is information — the element is preserved, just not editable");
            Assert.That(unsupported.Message, Does.Contain("heading").And.Contain("h1"));
        });
    }

    [Test]
    public void Check_WarnsWhenAFileHasNoFormShapeAtAll()
    {
        var findings = DesignCheck.CheckSource("Plain.bas", """
            Module Program
                Sub Main()
                    Console.WriteLine("nothing to design here")
                End Sub
            End Module
            """);

        Assert.That(findings.Single().Code, Is.EqualTo(DesignCodes.NoFormShape));
        Assert.That(findings.Single().IsWarning, Is.True,
            "an ordinary source file is not an error just because it is not a form");
    }

    [Test]
    public void Check_DoesNotThrow_OnSourceThatCannotParse()
    {
        Assert.DoesNotThrow(() => DesignCheck.CheckSource("Broken.bas", """
            Public Class MainForm
                Inherits Form
                Public Sub New()
                    If someCondition Then
            """));
    }

    // ==================================================================
    // The CLI verb — the only thing that proves the exit code is usable in CI
    // ==================================================================

    [Test]
    [Category("Integration")]
    public async Task Cli_DesignCheck_ExitsZeroAndPrintsASummary_ForACleanForm()
    {
        var path = Write("MainForm.bas", CleanForm);

        var (exitCode, stdout, stderr) = await CliTestHarness.RunCli(_dir, "design", "--check", path);

        Assert.That(exitCode, Is.Zero, $"stdout:\n{stdout}\nstderr:\n{stderr}");
        Assert.That(stdout, Does.Contain("0 error(s)"));
    }

    [Test]
    [Category("Integration")]
    public async Task Cli_DesignCheck_ExitsOne_AndStdoutCarriesFileLineAndColumn()
    {
        // The gate this verb exists for. Exit 0 is not a gate when the deliverable is a finding, and
        // a finding without a position is one nobody can act on.
        var path = Write("Handles.bas", FormWithHandles);

        var (exitCode, stdout, stderr) = await CliTestHarness.RunCli(_dir, "design", "--check", path);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(1),
                $"a design error must fail the command, or CI cannot use it.\nstdout:\n{stdout}\nstderr:\n{stderr}");
            Assert.That(stdout, Does.Contain("Handles.bas("), "stdout must carry the FILE");
            Assert.That(stdout, Does.Contain("(4,"), "stdout must carry the LINE");
            Assert.That(stdout, Does.Match(@"Handles\.bas\(4,\d+\): error BL8001:"),
                "the full click-to-navigate shape, with a column");
        });
    }

    [Test]
    [Category("Integration")]
    public async Task Cli_DesignCheck_ExitsZero_ForWarningsOnly()
    {
        // Warnings must not fail the build: an unsupported control type is information, and a verb
        // that failed on it could never be turned on in CI.
        var path = Write("Orphan.bas", """
            Public Class MainForm
                Inherits Form
                Private btnClick As Button
                Public Sub New()
                    btnClick = New Button()
                End Sub
            End Class
            """);

        var (exitCode, stdout, stderr) = await CliTestHarness.RunCli(_dir, "design", "--check", path);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero, $"stdout:\n{stdout}\nstderr:\n{stderr}");
            Assert.That(stdout, Does.Contain("BL8005"));
            Assert.That(stdout, Does.Contain("1 warning(s)"));
        });
    }

    [Test]
    [Category("Integration")]
    public async Task Cli_DesignCheck_ExitsTwo_ForAnArgumentError()
    {
        // 2 is reserved for argument errors so CI can tell "the tool was invoked wrongly" from
        // "the tool found something".
        var missing = Path.Combine(_dir, "does-not-exist.bas");

        var (noFile, _, _) = await CliTestHarness.RunCli(_dir, "design", "--check", missing);
        var (noFlag, _, _) = await CliTestHarness.RunCli(_dir, "design", missing);
        var (noPaths, _, _) = await CliTestHarness.RunCli(_dir, "design", "--check");

        Assert.Multiple(() =>
        {
            Assert.That(noFile, Is.EqualTo(2), "a missing input file is an argument error");
            Assert.That(noFlag, Is.EqualTo(2), "design without --check is an argument error");
            Assert.That(noPaths, Is.EqualTo(2), "--check with no files is an argument error");
        });
    }

    [Test]
    [Category("Integration")]
    public async Task Cli_DesignVerb_IsListedInTheUsage()
    {
        var (exitCode, stdout, _) = await CliTestHarness.RunCli(_dir, "--help");

        Assert.That(exitCode, Is.Zero);
        Assert.That(stdout, Does.Contain("design --check"),
            "a verb absent from --help is a verb nobody finds");
    }

    /// <summary>
    /// ⛔ The global flag sniff runs BEFORE the verb switch and uses <c>args.Contains</c> over the
    /// whole command line, so <c>design --check foo.bas -i</c> starts the REPL and never reaches the
    /// design handler. This test pins that the design verb's own flags stay clear of those names —
    /// it is the reason <c>--check</c> is spelled that way and why a <c>-v</c> "verbose" must never
    /// be added.
    /// </summary>
    [Test]
    public void DesignVerb_UsesNoFlagNameThatIsSniffedBeforeTheVerbSwitch()
    {
        string[] preEmptedFlags =
        {
            "--lsp", "--language-server", "--lsp-simple", "--debug-adapter", "--dap", "--dap-legacy",
            "--repl", "-i", "--interactive", "--help", "-h", "--version", "-v", "--parser-tests"
        };

        string[] designFlags = { "--check" };

        Assert.That(designFlags.Intersect(preEmptedFlags), Is.Empty,
            "a design flag sharing a name with a global flag would be swallowed before the verb " +
            "switch ever runs, and the verb would silently do something else entirely");
    }
}
