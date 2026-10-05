using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 21, the other half of "a form": <c>FormRetarget.ConvertToPair</c> produces the DOCUMENT and a
/// CODE-BEHIND for it — a fresh scaffold on the destination target with its designer regions already
/// generated and a stub for every handler that crossed.
///
/// <para>⛔⛔ Without the code-behind the retargeted document is a file the build compiles to
/// nothing: the region writer is what turns a document into a program, and it runs from the
/// designer's save or from nowhere. A retarget that wrote only the document would hand the user a
/// form whose <c>InitializeComponent</c> does not exist — the exact silent failure
/// <c>FormCodeBehind</c>'s own header records.</para>
///
/// <para>⚠ The ORIGINAL code-behind is never touched. Its class inherits a different base, its
/// handlers take a different signature, and it is the user's own code; converting it would be a
/// guess. The stubs are empty, and the finding summary says so.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class FormRetargetPairTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-retarget-" + Guid.NewGuid().ToString("N"));
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

    private const string WinFormsLogin = """
        <Form Name="LoginForm" Version="1" Width="400" Height="300" Text="Sign in">
          <Controls>
            <Label   Id="lblUser"  Text="User name" X="20" Y="20" Width="60"  Height="23" TabIndex="0"/>
            <TextBox Id="txtUser"  X="90" Y="20"  Width="200" Height="23" TabIndex="1">
              <Bind Event="TextChanged" Handler="txtUser_TextChanged"/>
            </TextBox>
            <Button  Id="btnLogin" Text="Sign in" X="190" Y="60" Width="100" Height="30" TabIndex="2">
              <Bind Event="Click" Handler="btnLogin_Click"/>
            </Button>
          </Controls>
          <Components/>
          <Resources/>
        </Form>
        """;

    private const string WebLogin = """
        <WebForm Name="LoginForm" Version="1">
          <Layout Kind="Grid" Cols="120px,1fr" Rows="auto,auto" Gap="8px"/>
          <Controls>
            <Label   Id="lblUser"  Text="User name" Col="0" Row="0" TabIndex="0"/>
            <TextBox Id="txtUser"  Col="1" Row="0" TabIndex="1">
              <Bind Event="input" Handler="txtUser_TextChanged"/>
            </TextBox>
            <Button  Id="btnLogin" Text="Sign in" Col="1" Row="1" TabIndex="2">
              <Bind Event="click" Handler="btnLogin_Click"/>
            </Button>
          </Controls>
          <Components/>
          <Resources/>
        </WebForm>
        """;

    /// <summary>
    /// A GroupBox wired on its DEFAULT event (Enter) and on a NON-default one (Click) — both are wired on both
    /// targets, so both binds cross (pre-flight B1). Code review follow-up 2026-09-29: the pair used to stub the
    /// default event only, so the crossed Click bind wired a Sub that did not exist.
    /// </summary>
    private const string WinFormsGroup = """
        <Form Name="GroupForm" Version="1" Width="400" Height="300">
          <Controls>
            <GroupBox Id="grp" Text="Options" X="10" Y="10" Width="200" Height="100" TabIndex="0">
              <Bind Event="Enter" Handler="grp_Enter"/>
              <Bind Event="Click" Handler="grp_Click"/>
            </GroupBox>
          </Controls>
        </Form>
        """;

    private const string WebGroup = """
        <WebForm Name="GroupForm" Version="1">
          <Layout Kind="Grid" Cols="1fr" Rows="auto" Gap="8px"/>
          <Controls>
            <GroupBox Id="grp" Text="Options" Col="0" Row="0" TabIndex="0">
              <Bind Event="focusin" Handler="grp_Enter"/>
              <Bind Event="click" Handler="grp_Click"/>
            </GroupBox>
          </Controls>
        </WebForm>
        """;

    /// <summary>Slice 5 Task 7: a form wired on its OWN Load and on a control's NON-default MouseDown.</summary>
    private const string WinFormsRootWired = """
        <Form Name="Login" Version="1" Width="400" Height="300" Text="Login">
          <Bind Event="Load" Handler="Login_Load"/>
          <Controls>
            <Button Id="btn" Text="Go" X="20" Y="20" Width="100" Height="30" TabIndex="0">
              <Bind Event="MouseDown" Handler="btn_MouseDown"/>
            </Button>
          </Controls>
        </Form>
        """;

    private const string WebRootWired = """
        <WebForm Name="Login" Version="1">
          <Bind Event="load" Handler="Login_Load"/>
          <Layout Kind="Grid" Cols="1fr" Rows="auto"/>
          <Controls>
            <Button Id="btn" Text="Go" Col="0" Row="0" TabIndex="0">
              <Bind Event="mousedown" Handler="btn_MouseDown"/>
            </Button>
          </Controls>
        </WebForm>
        """;

    private FormDocument Read(string xml, string fileName)
    {
        var file = FormDocumentReader.Read(Path.Combine(_dir, fileName), xml);
        Assert.That(file.IsRefused, Is.False, string.Join("; ", file.Diagnostics.Select(d => d.Format())));
        return file.Model;
    }

    /// <summary>1-based line of the first occurrence of <paramref name="needle"/>.</summary>
    private static int LineOf(string text, string needle)
    {
        var at = text.IndexOf(needle, StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThanOrEqualTo(0), $"'{needle}' is not in:\n{text}");
        return text.Substring(0, at).Count(c => c == '\n') + 1;
    }

    /// <summary>
    /// 1-based line of the init region's OPEN marker — from the scanner, not a text search: the
    /// scaffold's own comment says "init region" a few lines above the marker, and a search for the
    /// word found the comment.
    /// </summary>
    private static int InitMarkerLine(string code)
    {
        var init = RegionMarkers.Find(RegionMarkers.Scan(code), RegionMarkers.Init);
        Assert.That(init, Is.Not.Null, "the code-behind has no init region:\n" + code);
        return init!.Line;
    }

    // ==================================================================
    // The pair
    // ==================================================================

    [Test]
    public void ToWeb_ThePair_IsAWebDocumentAndAWebCodeBehind_WithTheRegionsGenerated()
    {
        var pair = FormRetarget.ConvertToPair(Read(WinFormsLogin, "LoginForm.blform"), FormTarget.Web);

        Assert.Multiple(() =>
        {
            Assert.That(pair.DocumentFileName, Is.EqualTo("LoginForm.blwebform"));
            Assert.That(pair.CodeFileName, Is.EqualTo("LoginForm.bas"));

            var reread = FormDocumentReader.Read(Path.Combine(_dir, pair.DocumentFileName), pair.DocumentText);
            Assert.That(reread.IsRefused, Is.False, "the document must be one its own reader accepts");
            Assert.That(reread.Model.Target, Is.EqualTo(FormTarget.Web));

            Assert.That(pair.CodeText, Does.Contain("Public Class LoginForm"));
            Assert.That(pair.CodeText, Does.Not.Contain("Inherits Form"), "a page is not a Form");

            // The regions are GENERATED, not left empty: the field, the element lookup, the wiring.
            Assert.That(pair.CodeText, Does.Contain("btnLogin"));
            Assert.That(pair.CodeText, Does.Contain("addEventListener(\"click\""),
                "the bind must be wired in the init region, or the page has a button that does nothing");
            Assert.That(pair.CodeText, Does.Contain("Me.InitializeComponent()"),
                "the scaffold's qualified call — an unqualified one is a ReferenceError on the JS backend");
        });
    }

    [Test]
    public void ToWeb_EveryHandlerThatCrossed_GetsAStub_AboveTheInitRegion()
    {
        // D8's ordering rule is real on the web: a Sub declared BELOW the region that wires it loses
        // its parameter types and addEventListener rejects the erased Action(Of Object).
        var pair = FormRetarget.ConvertToPair(Read(WinFormsLogin, "LoginForm.blform"), FormTarget.Web);
        var code = pair.CodeText;

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("Private Sub btnLogin_Click(e As DomEvent)"));
            Assert.That(code, Does.Contain("Private Sub txtUser_TextChanged(e As DomEvent)"));

            var init = InitMarkerLine(code);
            Assert.That(LineOf(code, "Sub btnLogin_Click"), Is.LessThan(init), "web: handlers precede the init region");
            Assert.That(LineOf(code, "Sub txtUser_TextChanged"), Is.LessThan(init));
        });
    }

    [Test]
    public void ToWinForms_ThePair_IsAFormDocumentAndAFormClass_WithStubsBelowTheInitRegion()
    {
        var pair = FormRetarget.ConvertToPair(Read(WebLogin, "LoginForm.blwebform"), FormTarget.WinForms);
        var code = pair.CodeText;

        Assert.Multiple(() =>
        {
            Assert.That(pair.DocumentFileName, Is.EqualTo("LoginForm.blform"));
            Assert.That(code, Does.Contain("Inherits Form"));
            Assert.That(code, Does.Contain("AddHandler btnLogin.Click, AddressOf btnLogin_Click"));
            Assert.That(code, Does.Contain("Private Sub btnLogin_Click(sender As Object, e As EventArgs)"));
            Assert.That(code, Does.Contain("Private Sub txtUser_TextChanged(sender As Object, e As EventArgs)"));

            // The canonical VSIX shape: InitializeComponent straight after New(), handlers below.
            Assert.That(LineOf(code, "Sub btnLogin_Click"), Is.GreaterThan(InitMarkerLine(code)));
        });
    }

    /// <summary>
    /// ⛔ Every bind that crossed gets its OWN stub, under its own handler name and with its own event's signature —
    /// not only the default event's. A crossed non-default bind with no stub wires a Sub that does not exist, and the
    /// pair stops compiling.
    /// </summary>
    [Test]
    public void EveryCrossedBind_DefaultOrNot_GetsItsOwnStub()
    {
        var toWinForms = FormRetarget.ConvertToPair(Read(WebGroup, "GroupForm.blwebform"), FormTarget.WinForms).CodeText;
        var toWeb = FormRetarget.ConvertToPair(Read(WinFormsGroup, "GroupForm.blform"), FormTarget.Web).CodeText;

        Assert.Multiple(() =>
        {
            Assert.That(toWinForms, Does.Contain("AddHandler grp.Click, AddressOf grp_Click"));
            Assert.That(toWinForms, Does.Contain("Private Sub grp_Click(sender As Object, e As EventArgs)"));
            Assert.That(toWinForms, Does.Contain("Private Sub grp_Enter(sender As Object, e As EventArgs)"));

            Assert.That(toWeb, Does.Contain("addEventListener(\"click\""));
            Assert.That(toWeb, Does.Contain("Private Sub grp_Click(e As DomEvent)"));
            Assert.That(toWeb, Does.Contain("Private Sub grp_Enter(e As DomEvent)"));
            Assert.That(LineOf(toWeb, "Sub grp_Click"), Is.LessThan(InitMarkerLine(toWeb)), "web: handlers precede the init region");

            foreach (var code in new[] { toWinForms, toWeb })
            {
                Assert.That(code.Split("Sub grp_Click(").Length - 1, Is.EqualTo(1), "one stub per handler, never two");
                Assert.That(code.Split("Sub grp_Enter(").Length - 1, Is.EqualTo(1));
            }
        });
    }

    /// <summary>
    /// ⛔ A crossed bind's stub takes ITS event's signature: a web Panel's Click arrives on a WinForms Panel whose
    /// DEFAULT is Paint, and a <c>PaintEventArgs</c> stub wired to Click would not compile (no relaxation to it).
    /// </summary>
    [Test]
    public void ACrossedNonDefaultBind_TakesItsOwnEventsSignature_NotTheDefaults()
    {
        var pair = FormRetarget.ConvertToPair(Read("""
            <WebForm Name="PanelForm" Version="1">
              <Layout Kind="Grid" Cols="1fr" Rows="auto" Gap="8px"/>
              <Controls>
                <Panel Id="pnl" Col="0" Row="0" TabIndex="0">
                  <Bind Event="click" Handler="pnl_Click"/>
                </Panel>
              </Controls>
            </WebForm>
            """, "PanelForm.blwebform"), FormTarget.WinForms);

        Assert.Multiple(() =>
        {
            Assert.That(pair.CodeText, Does.Contain("Private Sub pnl_Click(sender As Object, e As EventArgs)"));
            Assert.That(pair.CodeText, Does.Not.Contain("PaintEventArgs"));
        });
    }

    [Test]
    public void ThePairsRegions_AreCanon_SoTheDesignersFirstSave_WritesInsteadOfRefusing()
    {
        // A scaffold whose markers carried the wrong hash would make the designer report its OWN
        // output as a hand edit (BL8011) on the first save. The proof is that a regeneration from
        // the same document changes nothing and refuses nothing.
        foreach (var (xml, name, to) in new[]
                 {
                     (WinFormsLogin, "LoginForm.blform", FormTarget.Web),
                     (WebLogin, "LoginForm.blwebform", FormTarget.WinForms),
                     (FormRetargetTests.CanvasLogin, "LoginForm.blwebform", FormTarget.WinForms)
                 })
        {
            var pair = FormRetarget.ConvertToPair(Read(xml, name), to);
            var document = FormDocumentReader.Read(Path.Combine(_dir, pair.DocumentFileName), pair.DocumentText);

            var again = RegionWriter.Write(pair.CodeFileName, pair.CodeText, document.Model, pair.DocumentFileName);

            Assert.Multiple(() =>
            {
                Assert.That(again.Refused, Is.False,
                    $"{to}: " + string.Join("; ", again.Diagnostics.Select(d => d.Format())));
                Assert.That(again.Changed, Is.False, $"{to}: the regions must already say exactly this");
            });
        }
    }

    [Test]
    public void ThePair_CarriesTheConversionsFindings()
    {
        var pair = FormRetarget.ConvertToPair(Read(WinFormsLogin, "LoginForm.blform"), FormTarget.Web);

        Assert.That(pair.Diagnostics.Select(d => d.Code), Does.Contain(DesignCodes.RetargetLayoutCrossed),
            "the pair is what the CLI and the IDE hand the user; the loss must travel with it");
    }

    [Test]
    public void AFormWhoseNameCannotBeAClassName_IsRefusedBeforeAnythingIsProduced()
    {
        // The scaffolder's rule: a form name becomes a class name, and an underscore in a TYPE name
        // falls out of the compiler's .NET-type heuristic.
        var source = Read("""
            <Form Name="Main_Form" Version="1">
              <Controls><Button Id="b" X="8" Y="8" Width="75" Height="23" TabIndex="0"/></Controls>
            </Form>
            """, "Main_Form.blform");

        var ex = Assert.Throws<ArgumentException>(() => FormRetarget.ConvertToPair(source, FormTarget.Web));
        Assert.That(ex!.Message, Does.Contain("underscore"));
    }

    // ==================================================================
    // The pair must BUILD on its destination — and, where it is cheap, RUN
    // ==================================================================

    /// <summary>
    /// ⛔ A retargeted WinForms pair through the real compiler and csc. Nothing the retarget emits is
    /// falsifiable short of csc (the catalog's own header says why), so this is the gate.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void TheRetargetedWinFormsPair_CompilesThroughTheRealCompilerAndCsc()
    {
        var pair = FormRetarget.ConvertToPair(Read(WebLogin, "LoginForm.blwebform"), FormTarget.WinForms);

        var csharp = WinFormsCatalogSweepTests.CompileToCSharp(pair.CodeText);
        WinFormsCompile.AssertCompiles(csharp,
            "the code-behind a retarget produced for a WinForms form must be one csc accepts.");
    }

    /// <summary>⛔ The crossed non-default bind, through csc: a wired Sub that does not exist is BC30451 / CS0103.</summary>
    [Test]
    [Category("Integration")]
    public void ARetargetedPairWithANonDefaultBind_CompilesOnWinForms()
    {
        var pair = FormRetarget.ConvertToPair(Read(WebGroup, "GroupForm.blwebform"), FormTarget.WinForms);

        var csharp = WinFormsCatalogSweepTests.CompileToCSharp(pair.CodeText);
        WinFormsCompile.AssertCompiles(csharp,
            "a retargeted pair whose GroupBox is wired on Click (non-default) and Enter must be one csc accepts.");
    }

    /// <summary>⛔ The same, built by the real CLI as a JavaScript project — both handlers must exist and be wired.</summary>
    [Test]
    [Category("Integration")]
    public void ARetargetedPairWithANonDefaultBind_BuildsOnTheWeb()
    {
        var pair = FormRetarget.ConvertToPair(Read(WinFormsGroup, "GroupForm.blform"), FormTarget.Web);
        var (exit, output) = BuildWebPair(pair, "GroupForm");

        Assert.That(exit, Is.Zero, $"the real CLI refused the retargeted pair.\n{output}");
    }

    /// <summary>
    /// Slice 5 Task 7 (the <c>ConvertToPair</c> blocker): the ROOT's crossed bind gets its stub too — <c>Login_Load</c> in the
    /// web Load signature — beside the control's non-default <c>btn_MouseDown(e As DomEvent)</c>, both ABOVE the init region
    /// (the web ordering rule), and nothing is refused or named lost.
    /// </summary>
    [Test]
    public void ToWeb_ARootLoadBind_AndANonDefaultControlBind_BothGetStubsAboveTheRegion()
    {
        var pair = FormRetarget.ConvertToPair(Read(WinFormsRootWired, "Login.blform"), FormTarget.Web);
        var code = pair.CodeText;
        var init = InitMarkerLine(code);

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("Private Sub Login_Load()"), "the root's Load stub, in the page's Load signature");
            Assert.That(code, Does.Contain("Private Sub btn_MouseDown(e As DomEvent)"));
            Assert.That(LineOf(code, "Sub Login_Load("), Is.LessThan(init), "above the region that wires it");
            Assert.That(LineOf(code, "Sub btn_MouseDown("), Is.LessThan(init), "above the region that wires it");
            Assert.That(pair.Diagnostics.Select(d => d.Code), Has.None.EqualTo(DesignCodes.RetargetBindLost));
        });
    }

    /// <summary>
    /// ⛔ The same pair BUILT by the real CLI and RUN under node: the root Load stub (with the user's one line) runs on load,
    /// and the crossed MouseDown wiring compiles to a Sub that exists.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ToWeb_TheRootWiredPair_BuildsWithTheRealCli_AndItsLoadRunsUnderNode()
    {
        var pair = FormRetarget.ConvertToPair(Read(WinFormsRootWired, "Login.blform"), FormTarget.Web);
        var signature = pair.CodeText.Split('\n').First(l => l.Contains("Sub Login_Load("));
        var withLine = pair with { CodeText = pair.CodeText.Replace(signature, signature + "\n        Console.WriteLine(\"LOAD RAN\")") };

        var (exit, output) = BuildWebPair(withLine, "Login");
        Assert.That(exit, Is.Zero, $"the real CLI refused the retargeted pair.\n{output}");

        var ran = FormDesignerAcceptanceTests.RunPageUnderNode(Path.Combine(_dir, "bin", "Debug", "net8.0"), formName: "Login");
        if (ran == null)
        {
            Assert.Ignore("node is not on PATH, so the emitted page cannot be executed here");
        }

        Assert.Multiple(() =>
        {
            Assert.That(ran, Does.Not.Contain("ReferenceError"), "the retargeted page threw on load:\n" + ran);
            Assert.That(ran, Does.Contain("LOAD RAN"), "the crossed root Load did not reach its stub:\n" + ran);
        });
    }

    /// <summary>The reverse pair (a page wired on load and mousedown → a window) through the real compiler and csc.</summary>
    [Test]
    [Category("Integration")]
    public void ToWinForms_TheRootWiredPair_CompilesThroughTheRealCompilerAndCsc()
    {
        var pair = FormRetarget.ConvertToPair(Read(WebRootWired, "Login.blwebform"), FormTarget.WinForms);
        Assert.That(pair.CodeText, Does.Contain("Sub Login_Load(sender As Object, e As EventArgs)"), "the root's stub");

        var csharp = WinFormsCatalogSweepTests.CompileToCSharp(pair.CodeText);
        WinFormsCompile.AssertCompiles(csharp,
            "a retargeted window wired on its own Load and a Button's MouseDown must be one csc accepts.");
    }

    /// <summary>Writes a web pair into a JavaScript project and builds it with the real CLI.</summary>
    private (int Exit, string Output) BuildWebPair(FormRetargetPair pair, string startupForm)
    {
        File.WriteAllText(Path.Combine(_dir, pair.DocumentFileName), pair.DocumentText);
        File.WriteAllText(Path.Combine(_dir, pair.CodeFileName), pair.CodeText);
        File.WriteAllText(Path.Combine(_dir, "App.blproj"), $"""
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>JavaScript</TargetBackend>
                <StartupForm>{startupForm}</StartupForm>
              </PropertyGroup>
            </BasicLangProject>
            """);
        File.WriteAllText(Path.Combine(_dir, "Main.bas"), "Sub Main()\n    VgsForms.VgsDispatchForm()\nEnd Sub\n");

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", Path.Combine(_dir, "App.blproj") }, _dir, timeoutMs: 180_000);
        return (exit, stdout + "\n" + stderr);
    }

    /// <summary>
    /// Task 11: a Canvas page's pair is the window it was designed as — the exact client size, and each
    /// control's own Location/Size/Dock/Anchor, in the region the WinForms program runs.
    /// </summary>
    [Test]
    public void ACanvasPair_EmitsTheExactClientSizeAndGeometry()
    {
        var pair = FormRetarget.ConvertToPair(Read(FormRetargetTests.CanvasLogin, "LoginForm.blwebform"), FormTarget.WinForms);
        var code = pair.CodeText;

        Assert.Multiple(() =>
        {
            Assert.That(pair.DocumentFileName, Is.EqualTo("LoginForm.blform"));
            Assert.That(code, Does.Contain("Me.ClientSize = New Size(640, 400)"));
            Assert.That(code, Does.Contain("btnLogin.Location = New Point(540, 80)"));
            Assert.That(code, Does.Contain("btnLogin.Size = New Size(88, 30)"));
            Assert.That(code, Does.Contain("pnl.Dock = DockStyle.Top"));
            Assert.That(code, Does.Contain("btnLogin.Anchor = CType(10, AnchorStyles)"), "Bottom (2) | Right (8)");
            Assert.That(pair.Diagnostics.Select(d => d.Code), Has.None.EqualTo(DesignCodes.RetargetLayoutCrossed));
        });
    }

    /// <summary>⛔ The Canvas pair through the real compiler and csc — the WinForms program Tasks 12/13 compare the page against.</summary>
    [Test]
    [Category("Integration")]
    public void TheRetargetedCanvasPair_CompilesThroughTheRealCompilerAndCsc()
    {
        var pair = FormRetarget.ConvertToPair(Read(FormRetargetTests.CanvasLogin, "LoginForm.blwebform"), FormTarget.WinForms);

        var csharp = WinFormsCatalogSweepTests.CompileToCSharp(pair.CodeText);
        WinFormsCompile.AssertCompiles(csharp,
            "the code-behind a Canvas page's retarget produced must be one csc accepts.");
    }

    /// <summary>
    /// ⛔⛔ A retargeted web pair BUILT by the real CLI and RUN under node. This repo has shipped a
    /// green JavaScript build whose every page died on load; a retarget whose output only compiled
    /// would be exactly as trustworthy.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void TheRetargetedWebPair_BuildsWithTheRealCli_AndRunsUnderNode()
    {
        var pair = FormRetarget.ConvertToPair(Read(WinFormsLogin, "LoginForm.blform"), FormTarget.Web);

        File.WriteAllText(Path.Combine(_dir, pair.DocumentFileName), pair.DocumentText);

        // The user's one line, in the stub the retarget created — exactly as the acceptance
        // walkthrough does it, so that "HANDLER FIRED" proves the wiring reaches the Sub.
        var signature = pair.CodeText.Split('\n').First(l => l.Contains("Sub btnLogin_Click"));
        File.WriteAllText(Path.Combine(_dir, pair.CodeFileName),
            pair.CodeText.Replace(signature, signature + "\n        Console.WriteLine(\"HANDLER FIRED\")"));

        File.WriteAllText(Path.Combine(_dir, "App.blproj"), """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>JavaScript</TargetBackend>
                <StartupForm>LoginForm</StartupForm>
              </PropertyGroup>
            </BasicLangProject>
            """);
        File.WriteAllText(Path.Combine(_dir, "Main.bas"),
            "Sub Main()\n    VgsForms.VgsDispatchForm()\nEnd Sub\n");

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", Path.Combine(_dir, "App.blproj") }, _dir, timeoutMs: 180_000);

        Assert.That(exit, Is.Zero, $"the real CLI refused the retargeted pair.\n{stdout}\n{stderr}");
        Assert.That(stdout + stderr, Does.Not.Contain("BL8018"));

        var outDir = Path.Combine(_dir, "bin", "Debug", "net8.0");
        var page = Path.Combine(outDir, "LoginForm.html");
        Assert.That(File.Exists(page), Is.True, $"no page was emitted.\n{stdout}");

        var html = File.ReadAllText(page);
        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("User name"), "the Label crossed but is not on the page");
            Assert.That(html, Does.Contain("Sign in"), "the Button crossed but is not on the page");
        });

        var ran = FormDesignerAcceptanceTests.RunPageUnderNode(outDir);
        if (ran == null)
        {
            Assert.Ignore("node is not on PATH, so the emitted page cannot be executed here");
        }

        Assert.Multiple(() =>
        {
            Assert.That(ran, Does.Not.Contain("ReferenceError"), "the retargeted page threw on load:\n" + ran);
            Assert.That(ran, Does.Contain("HANDLER FIRED"), "the retargeted wiring did not reach the handler:\n" + ran);
        });
    }
}
