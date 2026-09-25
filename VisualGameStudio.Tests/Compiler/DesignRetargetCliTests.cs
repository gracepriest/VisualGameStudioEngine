using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>basiclang design --retarget &lt;form&gt; --out &lt;directory&gt;</c> — Task 21's entry point
/// outside the IDE, and the one that proves the exit codes are usable from a script.
///
/// <para>⛔ <c>--out</c> is REQUIRED, and that is a fact about the design rather than a CLI
/// preference: a form's document and code-behind pair by BASE NAME in one directory
/// (<c>FormCodeBehind.PathFor</c>), so writing <c>LoginForm.blwebform</c> beside
/// <c>LoginForm.blform</c> would pair the new page with the old window's class. The retargeted pair
/// needs a directory of its own, and the verb says so instead of guessing one.</para>
///
/// <para>Exit codes follow <c>--check</c>: <b>0</b> written, findings on stdout; <b>1</b> the source
/// was refused, nothing written; <b>2</b> an argument error, including a destination that exists.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class DesignRetargetCliTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-retarget-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    private const string WinFormsLogin = """
        <Form Name="LoginForm" Version="1" Width="400" Height="300" Text="Sign in">
          <Controls>
            <Label   Id="lblUser"  Text="User" X="20" Y="20" Width="60" Height="23" TabIndex="0"/>
            <Button  Id="btnLogin" Text="Sign in" X="190" Y="60" Width="100" Height="30" TabIndex="1">
              <Bind Event="Click" Handler="btnLogin_Click"/>
            </Button>
          </Controls>
        </Form>
        """;

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string Out => Path.Combine(_dir, "web");

    [Test]
    [Category("Integration")]
    public async Task Cli_DesignRetarget_WritesThePair_ListsTheLossOnStdout_AndExitsZero()
    {
        var source = Write("LoginForm.blform", WinFormsLogin);

        var (exit, stdout, stderr) = await CliTestHarness.RunCli(_dir, "design", "--retarget", source, "--out", Out);

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero, $"stdout:\n{stdout}\nstderr:\n{stderr}");
            Assert.That(File.Exists(Path.Combine(Out, "LoginForm.blwebform")), Is.True, "the document");
            Assert.That(File.Exists(Path.Combine(Out, "LoginForm.bas")), Is.True, "its code-behind");

            Assert.That(stdout, Does.Contain("BL8025"), "the loss at the hard edge belongs on stdout, like every finding");
            Assert.That(stdout, Does.Contain("'btnLogin'"), "…per control, by name");
            Assert.That(stdout, Does.Contain("LoginForm.blform"), "…against the SOURCE file, which is what the user has open");
            Assert.That(stdout, Does.Contain("warning(s)"), "and a summary");
            Assert.That(stdout, Does.Contain("not converted"),
                "the user must be told the original code-behind was left alone and the stubs are empty");
        });

        var code = File.ReadAllText(Path.Combine(Out, "LoginForm.bas"));
        Assert.That(code, Does.Contain("Private Sub btnLogin_Click(e As DomEvent)"),
            "the written code-behind carries the stub, not just the regions");
    }

    [Test]
    [Category("Integration")]
    public async Task Cli_DesignRetarget_TheOtherWay_WritesABlform()
    {
        var source = Write("LoginForm.blwebform", """
            <WebForm Name="LoginForm" Version="1">
              <Layout Kind="Grid"/>
              <Controls><Button Id="btn" Text="Go" Col="0" Row="0" TabIndex="0"/></Controls>
            </WebForm>
            """);

        var (exit, stdout, stderr) = await CliTestHarness.RunCli(_dir, "design", "--retarget", source, "--out", Out);

        Assert.That(exit, Is.Zero, $"stdout:\n{stdout}\nstderr:\n{stderr}");
        Assert.That(File.ReadAllText(Path.Combine(Out, "LoginForm.blform")), Does.StartWith("<Form "));
        Assert.That(File.ReadAllText(Path.Combine(Out, "LoginForm.bas")), Does.Contain("Inherits Form"));
    }

    [Test]
    [Category("Integration")]
    public async Task Cli_DesignRetarget_WithoutOut_IsAnArgumentError_ThatExplainsThePairing()
    {
        var source = Write("LoginForm.blform", WinFormsLogin);

        var (exit, _, stderr) = await CliTestHarness.RunCli(_dir, "design", "--retarget", source);

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(2));
            Assert.That(stderr, Does.Contain("--out"));
            Assert.That(File.Exists(Path.Combine(_dir, "LoginForm.blwebform")), Is.False,
                "nothing may be written beside the source: it would pair with the source's own .bas");
        });
    }

    [Test]
    [Category("Integration")]
    public async Task Cli_DesignRetarget_RefusesToOverwrite_AnExistingDestination()
    {
        var source = Write("LoginForm.blform", WinFormsLogin);
        Directory.CreateDirectory(Out);
        var existing = Path.Combine(Out, "LoginForm.bas");
        File.WriteAllText(existing, "' the user's own file\n");

        var (exit, _, stderr) = await CliTestHarness.RunCli(_dir, "design", "--retarget", source, "--out", Out);

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(2));
            Assert.That(stderr, Does.Contain("LoginForm.bas"), "the refusal names the file in the way");
            Assert.That(File.ReadAllText(existing), Is.EqualTo("' the user's own file\n"), "…and leaves it alone");
            Assert.That(File.Exists(Path.Combine(Out, "LoginForm.blwebform")), Is.False,
                "half a pair is worse than none: nothing is written when either file exists");
        });
    }

    [Test]
    [Category("Integration")]
    public async Task Cli_DesignRetarget_ARefusedSource_ExitsOne_AndWritesNothing()
    {
        // Two controls sharing an Id is a document refusal (BL8017). A retarget of a document the
        // designer itself will not open must not produce a second document it will not open either.
        var source = Write("LoginForm.blform", """
            <Form Name="LoginForm" Version="1">
              <Controls>
                <Button Id="b" X="8" Y="8" Width="75" Height="23" TabIndex="0"/>
                <Button Id="b" X="8" Y="40" Width="75" Height="23" TabIndex="1"/>
              </Controls>
            </Form>
            """);

        var (exit, stdout, _) = await CliTestHarness.RunCli(_dir, "design", "--retarget", source, "--out", Out);

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(stdout, Does.Contain("BL8017"), "the reader's refusal is the finding");
            Assert.That(Directory.Exists(Out) && Directory.GetFiles(Out).Length > 0, Is.False);
        });
    }

    [Test]
    [Category("Integration")]
    public async Task Cli_DesignRetarget_ArgumentErrors_ExitTwo()
    {
        var bas = Write("Main.bas", "Sub Main()\nEnd Sub\n");
        var form = Write("LoginForm.blform", WinFormsLogin);

        var (notADocument, _, notADocumentErr) = await CliTestHarness.RunCli(_dir, "design", "--retarget", bas, "--out", Out);
        var (bothVerbs, _, _) = await CliTestHarness.RunCli(_dir, "design", "--check", "--retarget", form, "--out", Out);
        var (noValue, _, _) = await CliTestHarness.RunCli(_dir, "design", "--retarget", form, "--out");
        var (twoFiles, _, _) = await CliTestHarness.RunCli(_dir, "design", "--retarget", form, form, "--out", Out);

        Assert.Multiple(() =>
        {
            Assert.That(notADocument, Is.EqualTo(2), "a .bas is not a form document");
            Assert.That(notADocumentErr, Does.Contain(".blform"));
            Assert.That(bothVerbs, Is.EqualTo(2), "--check and --retarget are different verbs");
            Assert.That(noValue, Is.EqualTo(2), "--out needs a directory");
            Assert.That(twoFiles, Is.EqualTo(2), "a retarget takes exactly one document");
        });
    }

    [Test]
    [Category("Integration")]
    public async Task Cli_DesignRetarget_IsListedInTheUsage()
    {
        var (exit, stdout, _) = await CliTestHarness.RunCli(_dir, "--help");

        Assert.That(exit, Is.Zero);
        Assert.That(stdout, Does.Contain("design --retarget"), "a verb absent from --help is a verb nobody finds");
    }
}
