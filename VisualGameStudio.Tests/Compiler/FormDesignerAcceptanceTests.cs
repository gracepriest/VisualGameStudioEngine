using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 27 — the acceptance walkthroughs: build a form through the designer, then BUILD AND RUN it.
///
/// <para>⛔⛔ <b>Everything else in this suite stops at "it compiles".</b> <c>WinFormsCompile</c> says
/// so in its own summary — <i>compile only, never run</i> — and until this fixture no form produced
/// by this designer had ever been executed on either target. That matters here more than most
/// places: this repo has already shipped a green build whose every page died on load with
/// <c>ReferenceError: VgsForms is not defined</c>, and a designer whose five finished components had
/// no caller. A build says nothing about either.</para>
///
/// <para>⚠ <b>What is and is not simulated.</b> These drive the designer's real commands — the same
/// ones the canvas gestures and the menus invoke — against the real view model, the real property
/// grid, the real scaffolder, the real region writer and the real CLI. They do NOT synthesize
/// operating-system drag events; pointer gestures are covered separately and headlessly by
/// <c>FormCanvasDropTests</c>, <c>FormCanvasMultiSelectTests</c> and <c>FormCanvasKeyboardTests</c>.
/// The claim here is "the designer's output builds and runs", not "the mouse works".</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class FormDesignerAcceptanceTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-accept-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        // ⚠ BL_KEEP_ACCEPTANCE keeps the artifacts. When one of these fails, the interesting thing
        // is the generated page or the generated C#, and a fixture that deletes them leaves you
        // guessing at exactly the moment it found something.
        if (Environment.GetEnvironmentVariable("BL_KEEP_ACCEPTANCE") == "1")
        {
            TestContext.Out.WriteLine($"[kept] {_dir}");
            return;
        }

        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>A file service over the real disk — this walkthrough must leave real files behind.</summary>
    private sealed class DiskFiles : IFileService
    {
        public Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(File.ReadAllText(path));

        public Task WriteFileAsync(string path, string content, CancellationToken cancellationToken = default)
        {
            File.WriteAllText(path, content);
            return Task.CompletedTask;
        }

        public Task<bool> FileExistsAsync(string path) => Task.FromResult(File.Exists(path));

        public Task<IEnumerable<string>> GetFilesAsync(string directory, string pattern, bool recursive = false) =>
            Task.FromResult<IEnumerable<string>>(Directory.GetFiles(
                directory, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));

        public Task<IEnumerable<string>> GetDirectoriesAsync(string directory) =>
            Task.FromResult<IEnumerable<string>>(Directory.GetDirectories(directory));

        public Task CreateDirectoryAsync(string path)
        {
            Directory.CreateDirectory(path);
            return Task.CompletedTask;
        }

        public Task DeleteFileAsync(string path)
        {
            File.Delete(path);
            return Task.CompletedTask;
        }

        public Task<DateTime> GetLastWriteTimeAsync(string path) =>
            Task.FromResult(File.GetLastWriteTime(path));

        // Watching is not part of a save-and-build walkthrough.
        public void WatchDirectory(string path, Action<string, FileChangeType> onChanged) { }

        public void StopWatching(string path) { }
    }

    private string Path_(string name) => Path.Combine(_dir, name);

    private void Write(string name, string content) => File.WriteAllText(Path_(name), content);

    private static void Log(string what) => TestContext.Out.WriteLine(what);

    /// <summary>
    /// The designer half, shared by both targets: new form, three controls, properties, double-click,
    /// one line of user code, save.
    /// </summary>
    private async Task<CodeEditorDocumentViewModel> BuildFormInDesignerAsync(FormTarget target)
    {
        // --- "New Form" in Solution Explorer ---
        var scaffold = FormScaffolder.Create("LoginForm", target);
        Write(scaffold.DocumentFileName, scaffold.DocumentText);
        Write(scaffold.CodeFileName, scaffold.CodeText);
        Log($"[1] new form      -> {scaffold.DocumentFileName} + {scaffold.CodeFileName}");

        var vm = new CodeEditorDocumentViewModel(new DiskFiles(), new Mock<IEventAggregator>().Object)
        {
            FilePath = Path_(scaffold.DocumentFileName)
        };
        vm.SetContent(scaffold.DocumentText);

        // --- drag three controls onto the surface ---
        // Pixel coordinates for WinForms; a web form places by CELL and ignores them.
        foreach (var (kind, x, y) in new[] { ("Label", 24, 24), ("TextBox", 120, 24), ("Button", 120, 72) })
        {
            var refusal = vm.PlaceControl(kind, x, y);
            Assert.That(refusal, Is.Null, $"placing a {kind} was refused: {refusal}");
        }

        var ids = vm.DesignDocument!.Controls.Select(c => c.Id).ToList();
        Log($"[2] placed        -> {string.Join(", ", ids)}");
        Assert.That(ids, Has.Count.EqualTo(3));

        // --- set properties, through the real property grid ---
        SetProperty(vm, ids[0], "Text", "User name");
        SetProperty(vm, ids[2], "Text", "Sign in");
        Log("[3] properties    -> Label.Text='User name', Button.Text='Sign in'");

        // --- double-click the Button: creates the handler and wires it ---
        var button = vm.DesignDocument!.Controls[2];
        await vm.ActivateControlCommand.ExecuteAsync(button);

        var bind = button.Binds.SingleOrDefault();
        Assert.That(bind, Is.Not.Null, "the double-click did not wire the button");
        Log($"[4] double-click  -> {bind!.Event} => {bind.Handler}");

        // --- the user writes one line in the handler ---
        var codePath = FormCodeBehind.PathFor(vm.FilePath!);
        var code = File.ReadAllText(codePath);
        var signature = code.Split('\n').First(l => l.Contains($"Sub {bind.Handler}"));
        var line = target == FormTarget.Web
            ? "        Console.WriteLine(\"HANDLER FIRED\")"
            : "        Console.WriteLine(\"HANDLER FIRED\")";
        File.WriteAllText(codePath, code.Replace(signature, signature + "\n" + line));
        Log($"[5] user code     -> one Console.WriteLine in {bind.Handler}");

        // --- Ctrl+S: writes the document AND regenerates the .bas regions ---
        Assert.That(await vm.SaveAsync(), Is.True, "the save failed");
        Log("[6] saved         -> document written, designer regions regenerated");

        return vm;
    }

    private static void SetProperty(CodeEditorDocumentViewModel vm, string id, string name, string value)
    {
        var control = vm.DesignDocument!.AllControls().Single(c => c.Id == id);
        vm.PropertyGrid.SelectedControl = control;

        var row = vm.PropertyGrid.Rows.FirstOrDefault(r =>
            string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

        Assert.That(row, Is.Not.Null, $"the property grid has no '{name}' row for '{id}'");
        row!.StringValue = value;
    }

    // ==================================================================
    // Walkthrough 1 — the web
    // ==================================================================

    /// <summary>
    /// ⛔⛔ RUNS the emitted page. A green JavaScript build proved nothing here before: the module
    /// dispatch compiled clean and every page died on load with <c>ReferenceError</c>.
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task Web_DesignedForm_BuildsAndRunsInABrowserRuntime()
    {
        await BuildFormInDesignerAsync(FormTarget.Web);

        Write("App.blproj", """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>JavaScript</TargetBackend>
                <StartupForm>LoginForm</StartupForm>
              </PropertyGroup>
            </BasicLangProject>
            """);
        // ⛔ The dispatch call is the user's responsibility and the build says so. Each page names
        // its form in <body data-form="…">, and NOTHING reads that attribute except the generated
        // VgsForms.VgsDispatchForm — so without this line every page loads and shows nothing.
        // Omitting it is what this walkthrough did first; the build caught it with BL8018.
        Write("Main.bas",
            "Sub Main()\n" +
            "    Console.WriteLine(\"App loaded\")\n" +
            "    VgsForms.VgsDispatchForm()\n" +
            "End Sub\n");

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", Path_("App.blproj") }, _dir, timeoutMs: 180_000);

        Log($"[7] build exit    -> {exit}");
        Assert.That(exit, Is.Zero, $"the real CLI refused the designer's output.\n{stdout}\n{stderr}");

        // ⚠ And it must be CLEAN. BL8018 is a warning, so a build that emits it still exits 0 —
        // and the page it produces does nothing at all.
        Assert.That(stdout + stderr, Does.Not.Contain("BL8018"),
            "the build warned that nothing calls the form dispatch, which means every page loads " +
            "and shows nothing");

        var outDir = Path.Combine(_dir, "bin", "Debug", "net8.0");
        var page = Path.Combine(outDir, "LoginForm.html");

        Assert.That(File.Exists(page), Is.True,
            $"no page was emitted. Build output:\n{stdout}\nFiles:\n" +
            string.Join("\n", Directory.Exists(outDir)
                ? Directory.GetFiles(outDir)
                : new[] { "(no output directory)" }));

        var html = File.ReadAllText(page);
        Log($"[8] page          -> LoginForm.html, {html.Length} bytes");

        // The controls the designer placed must be IN the page.
        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("User name"), "the Label's text is not on the page");
            Assert.That(html, Does.Contain("Sign in"), "the Button's text is not on the page");
            Assert.That(html, Does.Not.Contain("End Sub"), "unlowered BasicLang reached the page");
        });

        // ⛔ And it must RUN. Reading the script is exactly what missed the ReferenceError.
        var ran = RunPageUnderNode(outDir);
        if (ran == null)
        {
            Assert.Ignore("node is not on PATH, so the emitted page cannot be executed here");
        }

        Log($"[9] runtime       -> {ran!.Trim()}");
        Assert.Multiple(() =>
        {
            Assert.That(ran, Does.Not.Contain("ReferenceError"),
                "the page threw on load — a green build is not a running page");
            Assert.That(ran, Does.Contain("HANDLER FIRED"),
                "the click handler did not fire: the designer wired something that does not run");
        });
    }

    /// <summary>
    /// Loads the emitted page's script with a minimal DOM, clicks the button, and returns everything
    /// it printed. Returns null when node is absent.
    /// </summary>
    private string? RunPageUnderNode(string outDir)
    {
        var script = Directory.GetFiles(outDir, "*.js").FirstOrDefault();
        if (script == null)
        {
            Assert.Fail("the build emitted no JavaScript at all:\n" +
                        string.Join("\n", Directory.GetFiles(outDir)));
        }

        // A DOM stub just real enough for the generated dispatch: elements by id, addEventListener,
        // and a click() that invokes what was registered.
        File.WriteAllText(Path.Combine(outDir, "harness.mjs"), $$"""
            const els = new Map();
            const attrs = new Map();
            function make(id) {
              const handlers = {};
              const el = {
                id, value: "", textContent: "", style: {},
                addEventListener: (n, h) => { (handlers[n] ||= []).push(h); },
                // ⛔ getAttribute is LOAD-BEARING: the generated dispatch reads
                // document.body.getAttribute("data-form") to decide which form to construct. A stub
                // without it constructs nothing, wires nothing, and looks exactly like a page whose
                // handlers are broken.
                getAttribute: (n) => (attrs.get(id) || {})[n] ?? null,
                setAttribute: (n, v) => { attrs.set(id, { ...(attrs.get(id) || {}), [n]: v }); },
                dispatch: (n) => (handlers[n] || []).forEach(h => h({ target: el })),
                click: () => (handlers["click"] || []).forEach(h => h({ target: el })),
                appendChild: () => {}, querySelector: () => null
              };
              els.set(id, el);
              return el;
            }
            const body = make("body");
            body.setAttribute("data-form", "LoginForm");
            globalThis.document = {
              getElementById: (id) => els.get(id) || make(id),
              querySelector: () => null,
              createElement: () => make("created"),
              addEventListener: (n, h) => { if (n === "DOMContentLoaded") queueMicrotask(h); },
              body
            };
            globalThis.window = globalThis;
            try {
              await import('./{{Path.GetFileName(script!)}}');
            } catch (e) {
              console.log("LOAD ERROR: " + e);
            }
            // Click every button-ish element the page registered a click on.
            for (const [id, el] of els) { try { el.click(); } catch (e) { console.log("CLICK ERROR " + id + ": " + e); } }
            """);

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            "node", new[] { "harness.mjs" }, outDir, timeoutMs: 60_000);

        if (exit != 0 && (stderr.Contains("not recognized") || stderr.Contains("not found")))
        {
            return null;
        }

        return stdout + stderr;
    }

    // ==================================================================
    // Walkthrough 2 — WinForms
    // ==================================================================

    /// <summary>
    /// ⛔⛔ The first time a form this designer produced has been RUN as a window.
    ///
    /// <para>The program constructs the designed form, prints each control's RUNTIME bounds, invokes
    /// the button's click, and exits — so the recorded output answers all three questions at once:
    /// the controls exist, they are where the designer put them, and the handler is wired.</para>
    ///
    /// <para>⚠ The click is <c>PerformClick()</c>, not a human one. No UI automation is available
    /// here, and this is stated rather than implied: what is proven is that the wiring the designer
    /// generated reaches the handler, not that a physical mouse does.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task WinForms_DesignedForm_BuildsAndRunsAsARealWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("a WinForms window can only be run on Windows");
        }

        await BuildFormInDesignerAsync(FormTarget.WinForms);

        // Compile the designer's .bas to C# with the real CLI.
        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(),
            new[] { Path_("LoginForm.bas"), "--target=csharp" }, _dir, timeoutMs: 180_000);

        Log($"[7] compile exit  -> {exit}");
        Assert.That(exit, Is.Zero, $"the real CLI refused the designer's .bas.\n{stdout}\n{stderr}");

        var generated = Path_("LoginForm.cs");
        Assert.That(File.Exists(generated), Is.True, $"no C# emitted.\n{stdout}");

        var csharp = File.ReadAllText(generated);
        Assert.That(csharp, Does.Not.Contain("End Sub"), "unlowered BasicLang reached the C#");
        Log($"[8] generated C#  -> LoginForm.cs, {csharp.Length} bytes");

        // A real WinForms app around the generated form.
        var app = Path.Combine(_dir, "app");
        Directory.CreateDirectory(app);
        File.Copy(generated, Path.Combine(app, "LoginForm.cs"));

        File.WriteAllText(Path.Combine(app, "app.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWindowsForms>true</UseWindowsForms>
                <Nullable>disable</Nullable>
                <AssemblyName>DesignedApp</AssemblyName>
                <RootNamespace>DesignedApp</RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        // ⚠ The DRIVER is the only hand-written part. The form itself, its InitializeComponent and
        // its handler are entirely the designer's and the compiler's.
        File.WriteAllText(Path.Combine(app, "Driver.cs"), """
            using System;
            using System.Windows.Forms;

            // ⚠ The BasicLang C# backend emits into namespace GeneratedCode.
            using GeneratedCode;

            internal static class Driver
            {
                [STAThread]
                private static void Main()
                {
                    var form = new LoginForm();

                    // ⛔ The window is actually SHOWN. Button.PerformClick() checks CanSelect, which
                    // requires the control and every parent to be visible and enabled — so on a form
                    // that was never shown it silently does nothing, and the handler looks unwired
                    // when it is not. Measured: that is exactly what this walkthrough did first.
                    form.Show();
                    Application.DoEvents();

                    Console.WriteLine("SHOWN visible=" + form.Visible);
                    Console.WriteLine("FORM " + form.GetType().Name +
                                      " client=" + form.ClientSize.Width + "x" + form.ClientSize.Height);

                    Button button = null;
                    foreach (Control c in form.Controls)
                    {
                        Console.WriteLine("CONTROL " + c.Name + " type=" + c.GetType().Name +
                                          " text='" + c.Text + "'" +
                                          " at=" + c.Location.X + "," + c.Location.Y +
                                          " size=" + c.Size.Width + "x" + c.Size.Height);
                        if (c is Button b) { button = b; }
                    }

                    if (button == null)
                    {
                        Console.WriteLine("NO BUTTON");
                        return;
                    }

                    button.PerformClick();
                    Application.DoEvents();

                    form.Close();
                    Console.WriteLine("DONE");
                }
            }
            """);

        var (buildExit, buildOut, buildErr) = CliTestHarness.RunProcess(
            "dotnet", new[] { "build", "-c", "Release", "--nologo" }, app, timeoutMs: 300_000);

        Log($"[9] dotnet build  -> {buildExit}");
        Assert.That(buildExit, Is.Zero, $"the WinForms app did not build.\n{buildOut}\n{buildErr}");

        var exe = Directory
            .GetFiles(app, "DesignedApp.exe", SearchOption.AllDirectories)
            .FirstOrDefault();
        Assert.That(exe, Is.Not.Null, "no executable was produced");

        var (runExit, runOut, runErr) = CliTestHarness.RunProcess(exe!, Array.Empty<string>(), app, timeoutMs: 120_000);

        Log("[10] RUNTIME OUTPUT:");
        foreach (var line in (runOut + runErr).Replace("\r\n", "\n").Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                Log("     " + line);
            }
        }

        Assert.That(runExit, Is.Zero, $"the designed form crashed at run time.\n{runOut}\n{runErr}");

        Assert.Multiple(() =>
        {
            Assert.That(runOut, Does.Contain("CONTROL"), "the form built no controls at run time");
            Assert.That(runOut, Does.Contain("Sign in"),
                "the Button's designed Text did not survive to run time");
            Assert.That(runOut, Does.Contain("User name"),
                "the Label's designed Text did not survive to run time");
            Assert.That(runOut, Does.Contain("at=120,72"),
                "the Button is not where the designer put it");
            Assert.That(runOut, Does.Contain("HANDLER FIRED"),
                "the click handler did not fire: the designer wired something that does not run");
            Assert.That(runOut, Does.Contain("DONE"));
        });
    }
}
