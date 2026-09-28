using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Tests.Compiler.PixelLayout;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⛔⛔ Owner report 2026-09-28: "can't call Sub Main if I use a (web) form". Owner decision the same day: <b>Sub Main in
/// a web project with forms is STARTUP, like WinForms</b> — Main runs first, anything it adds to the page stays
/// visible, and the form then starts by itself (as <c>Application.Run(New Form1)</c> does on the desktop).
///
/// <para>Three defects made the owner's page blank, each measured on 4c34e1b3:</para>
/// <list type="number">
///   <item><description>nothing dispatched the form unless Main called <c>VgsForms.VgsDispatchForm()</c> — the build
///   said so only as warning BL8018;</description></item>
///   <item><description>a scaffold the designer had never saved called <c>Me.InitializeComponent()</c> with no such
///   method — <c>TypeError</c> on load the moment anything dispatched it, and BasicLang reported nothing;</description></item>
///   <item><description>a Canvas page's form area was <c>height: 100vh</c>, so what Main appended to the body landed
///   below the fold: a blank page with a scrollbar.</description></item>
/// </list>
///
/// <para>Everything here drives the REAL CLI against a REAL project and RUNS what it emits — under node, and laid out
/// by Microsoft Edge — because every one of those three was a green build.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class WebMainStartupTests
{
    private const int Viewport = 1024, ViewportH = 700;

    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-webmain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Environment.GetEnvironmentVariable("BL_KEEP_ACCEPTANCE") == "1")
        {
            TestContext.Out.WriteLine($"[kept] {_dir}");
            return;
        }

        try { Directory.Delete(_dir, true); } catch { }
    }

    private void Write(string name, string content) => File.WriteAllText(Path.Combine(_dir, name), content);

    private string OutputDir => Path.Combine(_dir, "bin", "Debug", "net8.0");

    /// <summary>
    /// The owner's Main, in the shape of the <c>new web</c> template: builds elements and appends them to the body —
    /// one AFTER the form area (appended) and one BEFORE it (prepended, through the <c>::</c> hatch: the typed
    /// <c>Element</c> declares no <c>prepend</c>).
    /// </summary>
    private const string TemplateMain = """
        Sub Main()
            Dim doc As Document = ::document

            Dim heading As Element = doc.createElement("h1")
            heading.id = "vgsMainHeading"
            heading.textContent = "Hello from App"
            doc.body.appendChild(heading)

            Dim button As Element = doc.createElement("button")
            button.id = "vgsMainButton"
            button.textContent = "Click me"
            doc.body.appendChild(button)

            Dim banner As Element = doc.createElement("header")
            banner.id = "vgsMainBanner"
            banner.textContent = "A banner above the form"
            ::document.body.prepend(banner)

            Console.WriteLine("MAIN RAN")
        End Sub

        """;

    /// <summary>
    /// A project exactly as the IDE's "Add New Form" leaves it: the scaffold's two files, NEVER saved by the designer,
    /// listed as &lt;Compile&gt; items beside <paramref name="main"/> (none when null). The only user edit is one line in
    /// the form's constructor, after <c>Me.InitializeComponent()</c>, so a run can SEE that the form was constructed.
    /// </summary>
    private FormDocument WriteOwnerProject(string? main)
    {
        var scaffold = FormScaffolder.Create("LoginForm", FormTarget.Web);
        Write(scaffold.DocumentFileName, scaffold.DocumentText);

        const string init = "        Me.InitializeComponent()\n";
        Assert.That(scaffold.CodeText, Does.Contain(init), "precondition: the scaffold's constructor");
        Write(scaffold.CodeFileName, scaffold.CodeText.Replace(init, init + "        Console.WriteLine(\"FORM SHOWN\")\n"));

        var items = new List<string> { scaffold.DocumentFileName, scaffold.CodeFileName };
        if (main != null)
        {
            Write("Main.bas", main);
            items.Insert(0, "Main.bas");
        }

        Write("App.blproj", $"""
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>JavaScript</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                {string.Join("\n    ", items.Select(i => $"<Compile Include=\"{i}\" />"))}
              </ItemGroup>
            </BasicLangProject>
            """);

        return FormDocumentReader.Read(scaffold.DocumentFileName, scaffold.DocumentText).Model;
    }

    private string BuildWithTheRealCli()
    {
        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", Path.Combine(_dir, "App.blproj") }, _dir, timeoutMs: 180_000);

        Assert.That(exit, Is.Zero, $"the real CLI refused the project.\n{stdout}\n{stderr}");
        Assert.That(File.Exists(Path.Combine(OutputDir, "LoginForm.html")), Is.True, $"no page was emitted.\n{stdout}");
        return stdout + stderr;
    }

    private string RunUnderNode()
    {
        var ran = FormDesignerAcceptanceTests.RunPageUnderNode(OutputDir);
        if (ran == null)
        {
            Assert.Ignore("node is not on PATH, so the emitted page cannot be executed here");
        }

        TestContext.Out.WriteLine("[node] " + ran!.Trim());
        return ran;
    }

    private static int Count(string haystack, string needle) => haystack.Split(needle).Length - 1;

    /// <summary>
    /// ⛔⛔ The owner's scenario, end to end under node: Main runs FIRST, then the never-saved scaffolded form starts by
    /// itself — no dispatch call anywhere in user code, no warning, no TypeError, and exactly one form.
    /// </summary>
    [Test]
    public void TheOwnersScenario_MainRunsFirst_ThenTheFormStartsByItself()
    {
        WriteOwnerProject(TemplateMain);
        var build = BuildWithTheRealCli();

        Assert.That(build, Does.Not.Contain(DesignCodes.DispatchNotCalled),
            "the automatic dispatch covers a project whose Main never calls it — there is nothing to warn about");

        var ran = RunUnderNode();
        Assert.Multiple(() =>
        {
            Assert.That(ran, Does.Not.Contain("LOAD ERROR"), "the page threw on load");
            Assert.That(ran, Does.Not.Contain("TypeError"),
                "a never-saved scaffold's InitializeComponent must exist (item 3)");
            Assert.That(ran, Does.Contain("MAIN RAN"), "Sub Main did not run");
            Assert.That(ran, Does.Contain("FORM SHOWN"), "the form never started: nothing dispatched it");
            Assert.That(Count(ran, "FORM SHOWN"), Is.EqualTo(1), "one page, one form");
            Assert.That(ran.IndexOf("MAIN RAN", StringComparison.Ordinal),
                Is.LessThan(ran.IndexOf("FORM SHOWN", StringComparison.Ordinal)),
                "Main runs FIRST, as WinForms' startup code runs before the form shows");
        });
    }

    /// <summary>
    /// ⛔ A user who DOES call the dispatch (every walkthrough before 2026-09-28 did, because BL8018 told them to) must
    /// not get the form twice: two constructions wire every handler twice and one click fires it twice.
    /// </summary>
    [Test]
    public void AnExplicitDispatchCall_StillStartsTheFormExactlyOnce()
    {
        WriteOwnerProject(
            "Sub Main()\n" +
            "    Console.WriteLine(\"MAIN RAN\")\n" +
            $"    {FormAssetEmitter.DispatchCall}\n" +
            "    Console.WriteLine(\"MAIN DONE\")\n" +
            "End Sub\n");
        BuildWithTheRealCli();

        var ran = RunUnderNode();
        Assert.Multiple(() =>
        {
            Assert.That(ran, Does.Not.Contain("LOAD ERROR"));
            Assert.That(Count(ran, "FORM SHOWN"), Is.EqualTo(1), "dispatched once per page, however many calls ask");
            Assert.That(ran.IndexOf("FORM SHOWN", StringComparison.Ordinal),
                Is.LessThan(ran.IndexOf("MAIN DONE", StringComparison.Ordinal)),
                "an explicit call starts the form where the user put it");
        });
    }

    /// <summary>A project with forms and NO Main (a site that is all forms) still starts its form, on load.</summary>
    [Test]
    public void AProjectWithNoMain_StartsItsFormOnLoad()
    {
        WriteOwnerProject(main: null);
        BuildWithTheRealCli();

        var ran = RunUnderNode();
        Assert.Multiple(() =>
        {
            Assert.That(ran, Does.Not.Contain("LOAD ERROR"));
            Assert.That(Count(ran, "FORM SHOWN"), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// ⛔⛔ And what Main put on the page is VISIBLE, laid out by a real browser at 1024×700: the heading and button it
    /// appended after the form area and the banner it prepended before it are all inside the viewport, the form area
    /// keeps at least its design size, and nothing scrolls.
    /// </summary>
    [Test]
    public void TheOwnersScenario_WhatMainAddsIsVisible_InEdge()
    {
        var form = WriteOwnerProject(TemplateMain);
        BuildWithTheRealCli();

        var edge = EdgeLayoutHarness.Measure(OutputDir, new[]
        {
            EdgeCase.Of(form, Viewport, ViewportH,
                EdgeStep.InViewport("heading", "vgsMainHeading"),
                EdgeStep.InViewport("button", "vgsMainButton"),
                EdgeStep.InViewport("banner", "vgsMainBanner"))
        });
        var result = edge.Results[$"LoginForm@{Viewport}x{ViewportH}"];
        var (designW, designH) = form.DesignSize;

        Assert.Multiple(() =>
        {
            Assert.That(result.Errors, Is.Empty, "the page threw in Edge");
            Assert.That(edge.ProfileDeleted, Is.True, $"the throw-away Edge profile is still there: {edge.ProfileDirectory}");
            Assert.That(result.Probes["heading"], Is.EqualTo("true"), "Main's heading is below the fold");
            Assert.That(result.Probes["button"], Is.EqualTo("true"), "Main's button is below the fold");
            Assert.That(result.Probes["banner"], Is.EqualTo("true"), "Main's banner is off screen");
            Assert.That(result.FormArea.Width, Is.EqualTo((double)Viewport), "the form area still spans the window");
            Assert.That(result.FormArea.Height, Is.GreaterThanOrEqualTo((double)designH),
                "the form area never squashes below its design height");
            Assert.That(result.FormArea.Y, Is.GreaterThan(0.0), "the prepended banner sits ABOVE the form area");
            Assert.That(result.ScrollHeight, Is.LessThanOrEqualTo((double)ViewportH),
                "the page must not scroll: there was room for Main's content beside the form");
            Assert.That(designW, Is.LessThanOrEqualTo(Viewport), "precondition: the design fits the window");
        });
    }
}
