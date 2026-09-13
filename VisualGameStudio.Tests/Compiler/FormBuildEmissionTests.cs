using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The markup emitter must actually run in a REAL build.
///
/// <para>⛔⛔ It did not. <c>JavaScriptEmitter.Emit</c> takes an optional <c>forms</c> argument and
/// <b>no production caller passed it</b> — not the CLI's project build, not the IDE's build
/// service. So <c>FormAssetEmitter.Emit</c> never ran outside its own tests: a project containing a
/// <c>.blwebform</c> built green and wrote no <c>.html</c> and no <c>.css</c>, and F5's
/// startup-page lookup always found zero pages. Every unit test passed the argument explicitly,
/// which is precisely how a dead production path stays green.</para>
///
/// <para>⚠ So this fixture drives the REAL CLI against a REAL project and looks at the files on
/// disk. Nothing here constructs an emitter, because constructing one is what hid the bug.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class FormBuildEmissionTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-formemit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    private void Write(string name, string content) =>
        File.WriteAllText(Path.Combine(_dir, name), content);

    private void WriteProject(params string[] compileItems)
    {
        var items = string.Join("\n    ", compileItems.Select(i => $"<Compile Include=\"{i}\" />"));
        Write("App.blproj", $"""
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>JavaScript</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                {items}
              </ItemGroup>
            </BasicLangProject>
            """);

        Write("Main.bas", "Sub Main()\n    Console.WriteLine(\"App loaded\")\nEnd Sub\n");
    }

    private const string LoginForm = """
        <WebForm Name="LoginForm" Version="1">
          <Layout Kind="Grid" Cols="120px,1fr" Rows="auto" Gap="8px"/>
          <Controls>
            <Label  Id="lblUser"  Text="User"    Col="0" Row="0" TabIndex="0"/>
            <Button Id="btnLogin" Text="Sign in" Col="1" Row="0" TabIndex="1"/>
          </Controls>
          <Components/>
          <Resources/>
        </WebForm>
        """;

    private (int Exit, string Out, string Err) Build() =>
        CliTestHarness.RunProcess(
            CliTestHarness.CliPath(),
            new[] { "build", Path.Combine(_dir, "App.blproj") }, _dir, timeoutMs: 120_000);

    private string OutputDir => Path.Combine(_dir, "bin", "Debug", "net8.0");

    [Test]
    public void ARealBuild_EmitsThePageForEachFormDocument()
    {
        WriteProject("Main.bas", "LoginForm.blwebform");
        Write("LoginForm.blwebform", LoginForm);

        var (exit, stdout, stderr) = Build();
        Assert.That(exit, Is.Zero, $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(OutputDir, "LoginForm.html")), Is.True,
                "the form's page must be written by a real build, not only by a unit test that "
                + "constructs the emitter itself");
            Assert.That(File.Exists(Path.Combine(OutputDir, "LoginForm.css")), Is.True);
        });
    }

    [Test]
    public void TheEmittedPage_CarriesTheFormsControls()
    {
        WriteProject("Main.bas", "LoginForm.blwebform");
        Write("LoginForm.blwebform", LoginForm);

        Assert.That(Build().Exit, Is.Zero);
        var html = File.ReadAllText(Path.Combine(OutputDir, "LoginForm.html"));

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("btnLogin"), "the control's id reaches the markup");
            Assert.That(html, Does.Contain("Sign in"), "and its text");
            Assert.That(html, Does.Contain("data-form"), "the dispatch attribute Main() reads");
        });
    }

    [Test]
    public void AProjectWithNoFormDocuments_StillBuildsAndEmitsNoPages()
    {
        // The guard against fixing this by making every build try to emit a page.
        WriteProject("Main.bas");

        Assert.That(Build().Exit, Is.Zero);

        Assert.That(Directory.GetFiles(OutputDir, "*.html").Select(Path.GetFileName),
            Is.EqualTo(new[] { "index.html" }),
            "only the harness — a project with no forms gains no form pages");
    }

    [Test]
    public void ARefusedFormDocument_WarnsAndDoesNotFailTheBuild()
    {
        // ⚠ A document that cannot be read is not a build failure: it is not a program, and the
        // compile routes already skip it. But the missing page must not be SILENT.
        WriteProject("Main.bas", "Broken.blwebform");
        Write("Broken.blwebform", """<WebForm Name="Broken" Version="99"><Controls/></WebForm>""");

        var (exit, stdout, stderr) = Build();

        Assert.That(exit, Is.Zero, "a form that cannot be read must not fail an otherwise good build");
        Assert.That(stdout + stderr, Does.Contain("Broken.blwebform"),
            "and the build must say why no page appeared");
        Assert.That(File.Exists(Path.Combine(OutputDir, "Broken.html")), Is.False);
    }
}
