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

    /// <summary>The DEFAULT project shape: no explicit &lt;Compile&gt; items, everything globbed.</summary>
    private void WriteGlobProject()
    {
        Write("App.blproj", """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>JavaScript</TargetBackend>
              </PropertyGroup>
            </BasicLangProject>
            """);
    }

    /// <summary>A form's code-behind: the class the dispatch constructs.</summary>
    private void WriteCodeBehind(string formName) =>
        Write(formName + ".bas", $"Public Class {formName}\n    Public Sub New()\n    End Sub\nEnd Class\n");

    private (int Exit, string Out, string Err) Build() =>
        CliTestHarness.RunProcess(
            CliTestHarness.CliPath(),
            new[] { "build", Path.Combine(_dir, "App.blproj") }, _dir, timeoutMs: 120_000);

    private string OutputDir => Path.Combine(_dir, "bin", "Debug", "net8.0");

    /// <summary>
    /// RUNS the emitted script, with a minimal <c>document</c> standing in for the browser, and
    /// returns whatever it wrote to stderr.
    ///
    /// <para>⛔⛔ <b>Reading the emitted JavaScript is what missed the worst defect on this
    /// branch.</b> The dispatch was generated from a <c>Public Module</c>, the build was green, and
    /// every string a test looked for was present — but the backend FLATTENS a module's members to
    /// bare globals while emitting the call site qualified, so the script referenced a
    /// <c>VgsForms</c> that appeared nowhere in the file and every page died on load with
    /// <i>ReferenceError: VgsForms is not defined</i>. No string assertion can see that. Executing
    /// it can.</para>
    ///
    /// <para>⚠ Returns null when node is not on PATH, so the fixture degrades to its other
    /// assertions on a machine without it rather than failing for the wrong reason.</para>
    /// </summary>
    private string? RunEmittedScript(string dataForm)
    {
        // The emitted file is an ES module; node needs the extension to treat it as one.
        File.Copy(Path.Combine(OutputDir, "App.js"), Path.Combine(OutputDir, "app.mjs"), overwrite: true);
        File.WriteAllText(Path.Combine(OutputDir, "harness.mjs"),
            "globalThis.document = { body: { getAttribute: () => " +
            $"{System.Text.Json.JsonSerializer.Serialize(dataForm)} }} }};\n" +
            "await import(\"./app.mjs\");\n");

        try
        {
            // Resolved off PATH; absence surfaces as the Win32Exception caught below rather than
            // as a hardcoded path that would silently be wrong on someone else's machine.
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(
                "node", new[] { "harness.mjs" }, OutputDir, timeoutMs: 30_000);

            return exit == 0 ? "" : stdout + stderr;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;   // no node on this machine
        }
    }

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

    [Test]
    public void AGlobProject_EmitsItsPagesToo()
    {
        // ⛔⛔ The DEFAULT project shape emitted NOTHING. Pages were driven off GetSourceFiles(),
        // whose glob cannot yield a .blwebform by design — that list feeds the lexer and a form
        // document is XML — so a project with explicit <Compile> items got pages and a project
        // with the same files and no <ItemGroup> got none, silently, with a green build.
        WriteGlobProject();
        Write("Main.bas", "Sub Main()\n    Console.WriteLine(\"App loaded\")\nEnd Sub\n");
        Write("LoginForm.blwebform", LoginForm);
        WriteCodeBehind("LoginForm");

        var (exit, stdout, stderr) = Build();
        Assert.That(exit, Is.Zero, $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        Assert.That(File.Exists(Path.Combine(OutputDir, "LoginForm.html")), Is.True,
            "a project with no explicit <Compile> items has the same forms as one that lists them");
    }

    [Test]
    public void ARealBuild_CompilesTheDispatchThatReadsDataForm()
    {
        // ⛔⛔ Every page carried <body data-form="..."> and NOTHING read it. DispatchSource had
        // no production caller at all: VgsDispatchForm was never generated, never compiled and
        // never shipped, so three forms produced three pages that all ran the same Main().
        WriteProject("Main.bas", "LoginForm.blwebform", "LoginForm.bas");
        Write("LoginForm.blwebform", LoginForm);
        WriteCodeBehind("LoginForm");
        Write("Main.bas", $"Sub Main()\n    {BasicLang.Forms.FormAssetEmitter.DispatchCall}\nEnd Sub\n");

        var (exit, stdout, stderr) = Build();
        Assert.That(exit, Is.Zero, $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var js = File.ReadAllText(Path.Combine(OutputDir, "App.js"));

        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("""getAttribute("data-form")"""),
                "the attribute every page carries is actually read by the shipped script");
            Assert.That(js, Does.Contain("new LoginForm()"),
                "and the named form is constructed");
        });
    }

    [Test]
    public void AProjectWhoseMainNeverCallsTheDispatch_IsWarnedAbout()
    {
        // ⚠ Generating the helper is only half the job — the backend emits one invocation, for
        // Main. A helper nobody calls is a page that loads a script and does nothing, from a build
        // that succeeded in every visible way.
        WriteProject("Main.bas", "LoginForm.blwebform", "LoginForm.bas");
        Write("LoginForm.blwebform", LoginForm);
        WriteCodeBehind("LoginForm");

        var (exit, stdout, stderr) = Build();

        Assert.That(exit, Is.Zero, "it is a warning, not a failure");
        Assert.That(stdout + stderr, Does.Contain(BasicLang.Forms.DesignCodes.DispatchNotCalled));
    }

    [Test]
    public void AFormWithNoCodeBehind_IsSkippedByTheDispatch_NotCompiledIntoAFailure()
    {
        // ⛔⛔ MEASURED against a real build: dispatching to a form whose class no file declares
        // emits `Dim f As New LoginForm()` and the build dies with BL7007 — reported against the
        // GENERATED file, which the user did not write and cannot open, for a problem that is
        // really "this .blwebform has no code-behind". The page is still emitted; the finding
        // belongs on the file the user can fix.
        WriteProject("Main.bas", "LoginForm.blwebform");
        Write("LoginForm.blwebform", LoginForm);

        var (exit, stdout, stderr) = Build();

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero, $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}");
            Assert.That(stdout + stderr, Does.Contain("LoginForm.bas"),
                "the message names the file that is missing");
            Assert.That(File.Exists(Path.Combine(OutputDir, "LoginForm.html")), Is.True,
                "the page is still generated");
        });
    }

    [Test]
    public void TwoForms_BothReachTheDispatch_AndItStillCompiles()
    {
        // ⛔⛔ The multi-form dispatch was only ever STRING-asserted — `DispatchSource` with two
        // names was checked for the right If/ElseIf text and never once handed to the compiler.
        // That is the exact shape of every dead-path defect on this branch: the generator's own
        // tests are green and nothing ever proves the output BUILDS. Each branch declares its own
        // `Dim f As New <Form>()` inside one Sub, which is precisely the kind of thing a string
        // assertion cannot judge.
        WriteProject("Main.bas", "LoginForm.blwebform", "LoginForm.bas",
                     "SignupForm.blwebform", "SignupForm.bas");
        Write("LoginForm.blwebform", LoginForm);
        Write("SignupForm.blwebform", LoginForm.Replace("LoginForm", "SignupForm"));
        WriteCodeBehind("LoginForm");
        WriteCodeBehind("SignupForm");
        Write("Main.bas", $"Sub Main()\n    {BasicLang.Forms.FormAssetEmitter.DispatchCall}\nEnd Sub\n");

        var (exit, stdout, stderr) = Build();
        Assert.That(exit, Is.Zero, $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var js = File.ReadAllText(Path.Combine(OutputDir, "App.js"));

        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("new LoginForm()"));
            Assert.That(js, Does.Contain("new SignupForm()"));
            Assert.That(File.Exists(Path.Combine(OutputDir, "LoginForm.html")), Is.True);
            Assert.That(File.Exists(Path.Combine(OutputDir, "SignupForm.html")), Is.True);
        });

        // ⛔⛔ And then RUN it, once per branch. The string assertions above are the same kind
        // that were green while the dispatch threw `ReferenceError` on every page load — and this
        // test was described in its own commit message as "runs the result" when it did no such
        // thing. Each `data-form` value takes a different branch, and a branch is only known to
        // work when it has been executed.
        foreach (var formName in new[] { "LoginForm", "SignupForm", "NoSuchForm" })
        {
            Assert.That(RunEmittedScript(formName), Is.Empty.Or.Null,
                $"the page for data-form=\"{formName}\" must run without throwing");
        }
    }

    [Test]
    public void TheEmittedScript_ACTUALLY_RUNS()
    {
        // ⛔⛔ THE defect this fixture exists for, in its purest form. The dispatch was generated
        // from a `Public Module`; the build was GREEN, every expected string was present, and the
        // page threw `ReferenceError: VgsForms is not defined` on load — because the backend
        // flattens a module's members to bare globals while emitting the call site qualified.
        // Reading the output cannot see that. Running it can.
        WriteProject("Main.bas", "LoginForm.blwebform", "LoginForm.bas");
        Write("LoginForm.blwebform", LoginForm);
        WriteCodeBehind("LoginForm");
        Write("Main.bas", $"Sub Main()\n    {BasicLang.Forms.FormAssetEmitter.DispatchCall}\nEnd Sub\n");

        var (exit, stdout, stderr) = Build();
        Assert.That(exit, Is.Zero, $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var failure = RunEmittedScript("LoginForm");
        if (failure == null)
        {
            Assert.Ignore("node is not on PATH, so the emitted script cannot be executed here");
        }

        Assert.That(failure, Is.Empty,
            "the page's own script must run without throwing — a green build does not say it does");
    }

    [Test]
    public void ASecondBuild_DoesNotCompileItsOwnGeneratedDispatchTwice()
    {
        // ⛔⛔ The source glob walked bin/ and obj/. The build writes VgsFormDispatch.g.bas into
        // obj/, so the SECOND build of a glob-shaped project swept its own output back in and
        // compiled the file twice — from a build that still reported success. Every test here ran
        // one build, which is exactly why nothing saw it.
        WriteGlobProject();
        Write("Main.bas", $"Sub Main()\n    {BasicLang.Forms.FormAssetEmitter.DispatchCall}\nEnd Sub\n");
        Write("LoginForm.blwebform", LoginForm);
        WriteCodeBehind("LoginForm");

        Assert.That(Build().Exit, Is.Zero, "first build");

        var (exit, stdout, stderr) = Build();

        Assert.That(exit, Is.Zero, $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        Assert.That(
            stdout.Split(BasicLang.Forms.FormDispatch.GeneratedFileName).Length - 1, Is.EqualTo(1),
            "the generated dispatch is compiled exactly once, however many times you build");
    }

    // ==================================================================
    // Followup 26 — the event name that reaches the browser
    // ==================================================================

    /// <summary>A form whose one bind names its event in the WINFORMS spelling — the copy-paste case.</summary>
    private const string WrongCaseEventForm = """
        <WebForm Name="LoginForm" Version="1">
          <Layout Kind="Grid" Cols="120px,1fr" Rows="auto" Gap="8px"/>
          <Controls>
            <Label  Id="lblUser"  Text="User"    Col="0" Row="0" TabIndex="0"/>
            <Button Id="btnLogin" Text="Sign in" Col="1" Row="0" TabIndex="1">
              <Bind Event="Click" Handler="btnLogin_Click"/>
            </Button>
          </Controls>
          <Components/>
          <Resources/>
        </WebForm>
        """;

    /// <summary>
    /// The code-behind the designer would leave on disk for <paramref name="documentText"/>: the real
    /// scaffold, the user's one-line handler ABOVE the init region (D8's ordering rule — on the web an
    /// <c>AddressOf</c> naming a later-declared Sub erases its parameter types), and the real
    /// <see cref="BasicLang.Forms.RegionWriter"/> filling both regions. Returns what the write reported,
    /// so a caller can assert on the diagnostics as well as on the page.
    /// </summary>
    private IReadOnlyList<BasicLang.Forms.DesignDiagnostic> WriteDesignedCodeBehind(string documentText)
    {
        var scaffold = BasicLang.Forms.FormScaffolder.Create("LoginForm", BasicLang.Forms.FormTarget.Web);

        const string anchor = "Public Class LoginForm\n";
        var handler =
            "\n    Private Sub btnLogin_Click(e As DomEvent)\n" +
            "        Console.WriteLine(\"HANDLER FIRED\")\n" +
            "    End Sub\n";

        // ⚠ A Replace that matched nothing would leave a code-behind with no handler at all, and the
        // build would then fail for a reason that has nothing to do with what this test asks.
        Assert.That(scaffold.CodeText, Does.Contain(anchor),
            "the web scaffold no longer opens with the class declaration this helper inserts after");

        var code = scaffold.CodeText.Replace(anchor, anchor + handler);

        var file = BasicLang.Forms.Serialization.FormDocumentReader.Read(
            Path.Combine(_dir, "LoginForm.blwebform"), documentText);
        Assert.That(file.IsRefused, Is.False, "the fixture's own document was refused by the reader");

        var result = BasicLang.Forms.RegionWriter.Write(
            Path.Combine(_dir, "LoginForm.bas"), code, file.Model, "LoginForm.blwebform");

        if (!result.Refused)
        {
            Write("LoginForm.bas", result.Text);
        }

        return result.Diagnostics;
    }

    /// <summary>Runs the built page under node, or returns null when node is not on this machine.</summary>
    private string? RunPage()
    {
        try
        {
            return FormDesignerAcceptanceTests.RunPageUnderNode(OutputDir);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// ⛔⛔ Followup 26. The document's event name reached <c>addEventListener</c> VERBATIM, and DOM
    /// event types are case-SENSITIVE — so a <c>.blwebform</c> carrying <c>Event="Click"</c> (the
    /// WinForms spelling, which is exactly what copying a <c>.blform</c> fixture gives you) emitted
    /// <c>addEventListener("Click", …)</c>. That registers cleanly, the build prints
    /// <i>Compilation successful!</i>, and the handler is simply never called.
    ///
    /// <para>⚠ <b>No string assertion can see this defect</b>, which is the whole reason this test is
    /// here rather than in <c>FormRegionWriterTests</c>. Every check that consults
    /// <c>RegionWriter.IsEmittedBind</c> compares the event name <c>OrdinalIgnoreCase</c>, so BL8028,
    /// the BL8013 ordering check and the web component's template wiring all agree that the bind IS
    /// correctly wired. Only the string that reaches the browser disagrees, so the browser is what has
    /// to be asked: the page is built by the REAL CLI and RUN, and the harness clicks with the real
    /// DOM event name <c>"click"</c>. A listener registered under <c>"Click"</c> is not called by it —
    /// exactly as in a browser.</para>
    /// </summary>
    [Test]
    public void AWrongCaseEventInTheDocument_StillReachesTheHandlerWhenTheBrowserClicks()
    {
        WriteProject("Main.bas", "LoginForm.blwebform", "LoginForm.bas");
        Write("LoginForm.blwebform", WrongCaseEventForm);

        // ⛔ "App loaded" is the POSITIVE CONTROL, and it is what makes a red here readable. Without
        // it an empty run means either "the handler was registered under a name the click never
        // matches" — the defect — or "the script never ran at all", and the two are indistinguishable
        // from an empty string. With it, the two failures say different things.
        Write("Main.bas",
            "Sub Main()\n" +
            "    Console.WriteLine(\"App loaded\")\n" +
            $"    {BasicLang.Forms.FormAssetEmitter.DispatchCall}\n" +
            "End Sub\n");

        var diagnostics = WriteDesignedCodeBehind(WrongCaseEventForm);
        Assert.That(diagnostics.Where(d => !d.IsWarning), Is.Empty,
            "the document names the Button's own default event, only in the wrong case — it must be "
            + "canonicalised, not refused: " + string.Join("; ", diagnostics.Select(d => d.Message)));

        var (exit, stdout, stderr) = Build();
        Assert.That(exit, Is.Zero, $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        var ran = RunPage();
        if (ran == null)
        {
            Assert.Ignore("node is not on PATH, so the emitted page cannot be executed here");
        }

        Assert.Multiple(() =>
        {
            Assert.That(ran, Does.Not.Contain("ReferenceError"),
                "the page threw on load — a green build is not a running page");
            Assert.That(ran, Does.Contain("App loaded"),
                "the emitted script did not run at all, so this test cannot say anything about the "
                + "handler. Fix the page before reading the assertion below.");
            Assert.That(ran, Does.Contain("HANDLER FIRED"),
                "the browser clicked btnLogin and the handler never ran. The document said "
                + "Event=\"Click\"; addEventListener is case-SENSITIVE, so a listener registered "
                + "under \"Click\" is never called by a real click. The emitter must canonicalise "
                + "the event through the catalog row's WebEvent instead of trusting the document.");
        });
    }
}
