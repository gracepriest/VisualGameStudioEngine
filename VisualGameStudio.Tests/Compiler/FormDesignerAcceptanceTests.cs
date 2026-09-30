using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.ViewModels.Documents;
using VisualGameStudio.Tests.Compiler.PixelLayout;

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

    /// <summary>
    /// A file service over the real disk — this walkthrough must leave real files behind.
    /// ⚠ <c>internal</c> so <c>FormComponentAcceptanceTests</c> drives the same designer over the
    /// same disk rather than a second stand-in.
    /// </summary>
    internal sealed class DiskFiles : IFileService
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
        // ⚠ Pinned to Grid (spec 2026-09-27 §2.5): this path was written against the Grid scaffold; the Canvas twin is Task 14.
        var scaffold = FormScaffolder.Create("LoginForm", target, FormLayoutKind.Grid);
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
        // ⛔ NO dispatch call (owner decision 2026-09-28: Sub Main is startup, like WinForms). Each
        // page names its form in <body data-form="…">, and the generated VgsForms.VgsDispatchForm
        // reads it — the JavaScript entry point now calls it after Main by itself. This walkthrough
        // once needed the explicit line (BL8018 caught its absence); the Canvas twin below keeps an
        // explicit call, so both shapes stay covered.
        Write("Main.bas",
            "Sub Main()\n" +
            "    Console.WriteLine(\"App loaded\")\n" +
            "End Sub\n");

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", Path_("App.blproj") }, _dir, timeoutMs: 180_000);

        Log($"[7] build exit    -> {exit}");
        Assert.That(exit, Is.Zero, $"the real CLI refused the designer's output.\n{stdout}\n{stderr}");

        // ⚠ And it must be CLEAN: BL8018 is retired — the entry point starts the form itself.
        Assert.That(stdout + stderr, Does.Not.Contain("BL8018"),
            "the retired 'nothing calls the form dispatch' warning came back");

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
    /// it printed. Returns null when a shell-resolved node reports itself missing on stderr; a node that cannot be
    /// started at all skips the test (below).
    ///
    /// <para>⚠ <c>internal static</c> so <c>FormRetargetPairTests</c> runs a RETARGETED page through
    /// the same harness rather than a second DOM stub that could drift from this one. It assumes a
    /// form named <paramref name="formName"/> (default <c>LoginForm</c>), as the <c>data-form</c> line
    /// below says.</para>
    ///
    /// <para><paramref name="clickId"/> null (the default) clicks every element the page registered,
    /// exactly as before. A non-null id clicks ONLY that element — and if the page never registered an
    /// element under that id, prints <c>NO ELEMENT &lt;clickId&gt;</c> so a miss is visible rather than
    /// silently clicking nothing.</para>
    ///
    /// <para>⛔ Node absent SKIPS the test (<see cref="TestSkip.IgnoreEvenInsideMultiple"/>), never errors: when the
    /// executable is missing, <c>Process.Start</c> throws <see cref="System.ComponentModel.Win32Exception"/> before any
    /// "not recognized" text could reach stderr (Task 14 review). <paramref name="node"/> is the seam that proves it.</para>
    /// </summary>
    internal static string? RunPageUnderNode(
        string outDir, string formName = "LoginForm", string? clickId = null, string node = "node",
        (string Id, string Event)? dispatch = null)
    {
        var script = Directory.GetFiles(outDir, "*.js").FirstOrDefault();
        if (script == null)
        {
            Assert.Fail("the build emitted no JavaScript at all:\n" +
                        string.Join("\n", Directory.GetFiles(outDir)));
        }

        var clickScript = clickId == null
            ? "for (const [id, el] of els) { try { el.click(); } catch (e) { console.log(\"CLICK ERROR \" + id + \": \" + e); } }"
            : $"if (els.has(\"{clickId}\")) {{ try {{ els.get(\"{clickId}\").click(); }} catch (e) {{ console.log(\"CLICK ERROR {clickId}: \" + e); }} }} else {{ console.log(\"NO ELEMENT {clickId}\"); }}";

        // Slice 3: an event other than click (a GroupBox's Enter is the fieldset's `focusin`), dispatched AFTER the clicks
        // through the stub's own dispatch — so a handler registered on that DOM event name is the one that runs.
        if (dispatch is { } d)
        {
            clickScript += $"\nif (els.has(\"{d.Id}\")) {{ try {{ els.get(\"{d.Id}\").dispatch(\"{d.Event}\"); }} catch (e) {{ console.log(\"DISPATCH ERROR {d.Id}: \" + e); }} }} else {{ console.log(\"NO ELEMENT {d.Id}\"); }}";
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
                appendChild: () => {}, prepend: () => {}, querySelector: () => null
              };
              els.set(id, el);
              return el;
            }
            const body = make("body");
            body.setAttribute("data-form", "{{formName}}");
            globalThis.document = {
              getElementById: (id) => els.get(id) || make(id),
              querySelector: () => null,
              createElement: () => make("created"),
              addEventListener: (n, h) => { if (n === "DOMContentLoaded") queueMicrotask(h); },
              body
            };
            globalThis.window = globalThis;
            // Task 25: a Timer is a setInterval handle. These REPLACE node's real timers for this
            // process (window IS globalThis), which is deliberate — a real interval would keep the
            // process alive until the harness timeout. The stub fires the callback once, so a tick
            // handler that prints proves the wiring reached it.
            globalThis.setInterval = (cb, ms) => { console.log("setInterval " + ms); cb(); return 7; };
            globalThis.clearInterval = (id) => { console.log("clearInterval " + id); };
            try {
              await import('./{{Path.GetFileName(script!)}}');
            } catch (e) {
              console.log("LOAD ERROR: " + e);
            }
            {{clickScript}}
            """);

        int exit;
        string stdout, stderr;
        try
        {
            (exit, stdout, stderr) = CliTestHarness.RunProcess(
                node, new[] { "harness.mjs" }, outDir, timeoutMs: 60_000);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            TestSkip.IgnoreEvenInsideMultiple($"'{node}' could not be started ({e.Message}), so the emitted page cannot be executed here");
            throw; // unreachable
        }

        if (exit != 0 && (stderr.Contains("not recognized") || stderr.Contains("not found")))
        {
            return null;
        }

        return stdout + stderr;
    }

    // ==================================================================
    // Walkthrough 1b — a Canvas page (spec 2026-09-27 §7 "End to end", Task 14)
    // ==================================================================

    /// <summary>
    /// Selects <paramref name="control"/> through the ONE selection store and sets a row of the real property grid.
    /// ⛔ Never <c>PropertyGrid.SelectedControl</c> directly (CLAUDE.md): the grid follows <c>Selection.Changed</c>.
    /// </summary>
    private static void SetThroughGrid(CodeEditorDocumentViewModel vm, FormControl control, string name, string value)
    {
        vm.Selection.Set(control);
        Assert.That(vm.PropertyGrid.SelectedControl, Is.SameAs(control),
            $"selecting '{control.Id}' through the selection store did not put it in the property grid");

        var row = vm.PropertyGrid.Rows.FirstOrDefault(r =>
            string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        Assert.That(row, Is.Not.Null, $"the property grid has no '{name}' row for '{control.Id}'");
        row!.StringValue = value;
    }

    /// <summary>
    /// ⛔⛔ The Canvas twin of <see cref="Web_DesignedForm_BuildsAndRunsInABrowserRuntime"/>: a NEW web form (the
    /// Canvas default, not the Grid pin above) designed through the real designer commands — pixel drops, grid edits of
    /// Text/Anchor/Dock, a MenuStrip with a Fill Panel under it, a double-click — saved, built by the real CLI as a
    /// JavaScript project, RUN under node (the handler fires), and LAID OUT by Microsoft Edge at the design size and
    /// wider, each held to the model and to the real WinForms window of the same saved form at ±1px.
    ///
    /// <para>Pre-flight <c>2026-09-27-web-pixel-layout-task14-preflight.md</c>: node runs BEFORE Edge (both put a
    /// <c>.js</c> in the output directory), and the WinForms reference works in its own subdirectory.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task Web_Canvas_DesignedForm_BuildsRunsAndLaysOut()
    {
        const int DesignW = 800, DesignH = 450, WideW = 1000, WideH = 600;

        // --- "New Form": the DEFAULT web scaffold (Canvas) ---
        var scaffold = FormScaffolder.Create("LoginForm", FormTarget.Web);
        Write(scaffold.DocumentFileName, scaffold.DocumentText);
        Write(scaffold.CodeFileName, scaffold.CodeText);
        Log($"[1] new form      -> {scaffold.DocumentFileName} + {scaffold.CodeFileName}");

        var vm = new CodeEditorDocumentViewModel(new DiskFiles(), new Mock<IEventAggregator>().Object)
        {
            FilePath = Path_(scaffold.DocumentFileName)
        };
        vm.SetContent(scaffold.DocumentText);
        Assert.That(vm.DesignDocument, Is.Not.Null, "the new form did not open in the designer");

        // --- a MenuStrip with one item, then a Panel docked Fill under it ---
        Assert.That(vm.PlaceControl("MenuStrip", 0, 0), Is.Null, "placing the MenuStrip was refused");
        var menu = vm.DesignDocument!.Controls.Single(c => c.Kind == "MenuStrip");
        vm.BeginTypeHere(menu);
        vm.CommitTypeHere("File");
        vm.CancelTypeHere();
        Assert.That(menu.Children.Select(c => c.Kind), Is.EqualTo(new[] { "ToolStripMenuItem" }), "Type Here added no item");

        Assert.That(vm.PlaceControl("Panel", 100, 100), Is.Null, "placing the Panel was refused");
        var panel = vm.DesignDocument!.Controls.Single(c => c.Kind == "Panel");
        SetThroughGrid(vm, panel, "Dock", "Fill");

        // --- three controls dropped at pixel points OVER the Fill panel: they land inside it ---
        foreach (var (kind, x, y) in new[] { ("Label", 24, 48), ("TextBox", 120, 48), ("Button", 120, 96) })
        {
            var refusal = vm.PlaceControl(kind, x, y);
            Assert.That(refusal, Is.Null, $"placing a {kind} was refused: {refusal}");
        }

        Assert.That(vm.DesignDocument!.Controls.Select(c => c.Id), Is.EqualTo(new[] { "MenuStrip1", "Panel1" }),
            "top level: the strip and the Fill panel only");
        var label = panel.Children.Single(c => c.Kind == "Label");
        var textBox = panel.Children.Single(c => c.Kind == "TextBox");
        var button = panel.Children.Single(c => c.Kind == "Button");
        Assert.That(new[] { label.Id, textBox.Id, button.Id }, Is.EqualTo(new[] { "Label1", "TextBox1", "Button1" }));
        Assert.That(new[] { label, textBox, button }.Select(c => c.Geometry), Is.All.InstanceOf<PixelGeometry>(),
            "a Canvas page stores pixels");
        Log($"[2] placed        -> MenuStrip1 (+File), Panel1 Dock=Fill holding Label1, TextBox1, Button1");

        // --- properties, through the real grid ---
        SetThroughGrid(vm, label, "Text", "User name");
        SetThroughGrid(vm, button, "Text", "Sign in");
        SetThroughGrid(vm, textBox, "Anchor", "Top, Left, Right");
        SetThroughGrid(vm, button, "Anchor", "Top, Right");
        Log("[3] properties    -> Text x2, TextBox Anchor=Top,Left,Right, Button Anchor=Top,Right");

        // --- double-click the Button ---
        await vm.ActivateControlCommand.ExecuteAsync(button);
        var bind = button.Binds.SingleOrDefault();
        Assert.That(bind, Is.Not.Null, "the double-click did not wire the button");
        Log($"[4] double-click  -> {bind!.Event} => {bind.Handler}");

        // --- one line of user code, then Ctrl+S ---
        var codePath = FormCodeBehind.PathFor(vm.FilePath!);
        var code = File.ReadAllText(codePath);
        var signature = code.Split('\n').First(l => l.Contains($"Sub {bind.Handler}"));
        File.WriteAllText(codePath, code.Replace(signature, signature + "\n        Console.WriteLine(\"HANDLER FIRED\")"));
        Assert.That(await vm.SaveAsync(), Is.True, "the save failed");
        Log("[5] saved");

        // What the CLI will build: the SAVED document, read back from disk.
        var saved = FormDocumentReader.Read(vm.FilePath!, File.ReadAllText(vm.FilePath!)).Model;
        Log("[5] saved form    ->\n" + File.ReadAllText(vm.FilePath!));

        // ⛔ The grid's Text edits reached the file (review: a page that dropped every caption stayed green).
        Assert.Multiple(() =>
        {
            Assert.That(saved.FindById("Label1")?.Properties.GetValueOrDefault("Text"), Is.EqualTo("User name"),
                "the Label's grid-set Text is not in the saved form");
            Assert.That(saved.FindById("Button1")?.Properties.GetValueOrDefault("Text"), Is.EqualTo("Sign in"),
                "the Button's grid-set Text is not in the saved form");
        });

        // --- a real CLI build of a JavaScript project ---
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
        Write("Main.bas",
            "Sub Main()\n" +
            "    Console.WriteLine(\"App loaded\")\n" +
            "    VgsForms.VgsDispatchForm()\n" +
            "End Sub\n");

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", Path_("App.blproj") }, _dir, timeoutMs: 180_000);
        Log($"[6] build exit    -> {exit}");
        Assert.That(exit, Is.Zero, $"the real CLI refused the designer's output.\n{stdout}\n{stderr}");
        Assert.That(stdout + stderr, Does.Not.Contain("BL8018"), "nothing calls the form dispatch: the page would show nothing");

        var outDir = Path.Combine(_dir, "bin", "Debug", "net8.0");
        Assert.That(File.Exists(Path.Combine(outDir, "LoginForm.html")), Is.True, $"no page was emitted.\n{stdout}");
        var html = File.ReadAllText(Path.Combine(outDir, "LoginForm.html"));
        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("User name"), "the Label's text is not on the built page");
            Assert.That(html, Does.Contain("Sign in"), "the Button's text is not on the built page");
        });

        // --- ⛔ RUN it (before Edge writes its own .js here — pre-flight B1) ---
        var ran = RunPageUnderNode(outDir, clickId: "Button1");
        if (ran == null)
        {
            Assert.Ignore("node is not on PATH, so the emitted page cannot be executed here");
        }

        Log($"[7] runtime       -> {ran!.Trim()}");
        Assert.Multiple(() =>
        {
            Assert.That(ran, Does.Not.Contain("LOAD ERROR"), "the page's script threw on load");
            Assert.That(ran, Does.Not.Contain("ReferenceError"), "a green build is not a running page");
            Assert.That(ran, Does.Not.Contain("CLICK ERROR"), "the click handler threw");
            Assert.That(ran, Does.Not.Contain("NO ELEMENT"), "the page registered nothing under Button1");
            Assert.That(ran, Does.Contain("HANDLER FIRED"), "the click handler did not fire");
            // ⛔ Main calls the dispatch EXPLICITLY here and the entry point calls it again after Main: the dispatch
            // runs once per page, so the form is built once and one click fires its handler once — not twice.
            Assert.That(ran!.Split("HANDLER FIRED").Length - 1, Is.EqualTo(1),
                "the form was dispatched twice: every handler wired twice, one click fired it twice");
        });

        // --- ⛔ LAY IT OUT in a real browser: the design size, and wider ---
        var edge = EdgeLayoutHarness.Measure(outDir, new[]
        {
            EdgeCase.Of(saved, DesignW, DesignH,
                EdgeStep.HasText("labelText", "User name"), EdgeStep.HasText("buttonText", "Sign in")),
            EdgeCase.Of(saved, WideW, WideH)
        });
        var atDesign = edge.Results[$"LoginForm@{DesignW}x{DesignH}"];
        var atWide = edge.Results[$"LoginForm@{WideW}x{WideH}"];

        // --- and the real WinForms window of the same saved form ---
        var window = WinFormsReferenceHarness.Measure(Path.Combine(_dir, "winforms"),
            new ReferenceFixture(saved, new ResizeStep("grow", WideW, WideH)))["LoginForm"];

        foreach (var (what, snapshot) in new[]
                 {
                     ("Edge design", atDesign["initial"]), ("Edge wide", atWide["initial"]),
                     ("WinForms design", window["design"]), ("WinForms grow", window["grow"])
                 })
        {
            Log($"[8] {what} {snapshot.ClientWidth}x{snapshot.ClientHeight}");
            foreach (var (id, box) in snapshot.Controls.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                Log($"      {id,-11} {LayoutComparison.Show(box)}");
            }
        }

        void Agrees(string what, IReadOnlyDictionary<string, LayoutBox> expected, LayoutSnapshot measured)
        {
            var differences = EdgeLayoutHarness.Differences(expected, measured.Controls);
            Assert.That(differences, Is.Empty, $"{what}: Edge disagrees:\n  " + string.Join("\n  ", differences));
        }

        var design = atDesign["initial"].Controls;
        var wide = atWide["initial"].Controls;
        Assert.Multiple(() =>
        {
            foreach (var result in new[] { atDesign, atWide })
            {
                Assert.That(result.Errors, Is.Empty, $"{result.Case.Name}: the page threw in Edge");
                Assert.That((result.FormArea.X, result.FormArea.Y), Is.EqualTo((0.0, 0.0)), $"{result.Case.Name}: the form area moved");
                Assert.That(result.FormClientOrigin, Is.EqualTo((0.0, 0.0)), $"{result.Case.Name}: the form area has a border");
            }

            Assert.That(edge.ProfileDeleted, Is.True, $"the throw-away Edge profile is still there: {edge.ProfileDirectory}");
            Assert.That(atDesign.Probes["labelText"], Is.EqualTo("true"), "Edge rendered no 'User name' in the form area");
            Assert.That(atDesign.Probes["buttonText"], Is.EqualTo("true"), "Edge rendered no 'Sign in' in the form area");

            // Against the model and the window, at both sizes (spec §7).
            Agrees("design vs model", PixelLayoutModel.Rects(saved, FormDockMode.Runtime, (DesignW, DesignH)), atDesign["initial"]);
            Agrees("wide vs model", PixelLayoutModel.Rects(saved, FormDockMode.Runtime, (WideW, WideH)), atWide["initial"]);
            Agrees("design vs WinForms", window["design"].Controls, atDesign["initial"]);
            Agrees("wide vs WinForms", window["grow"].Controls, atWide["initial"]);

            // ⚠ And the numbers themselves, so a model and a harness that were wrong TOGETHER cannot agree vacuously.
            Assert.That(design["MenuStrip1"], Is.EqualTo(new LayoutBox(0, 0, DesignW, 24)), "the strip: full width, its catalog height");
            Assert.That(design["Panel1"], Is.EqualTo(new LayoutBox(0, 24, DesignW, DesignH - 24)), "Fill: the client area under the strip");
            Assert.That(wide["Panel1"], Is.EqualTo(new LayoutBox(0, 24, WideW, WideH - 24)), "Fill follows the window");
            Assert.That(design["Label1"], Is.EqualTo(new LayoutBox(24, 48, 100, 23)), "the Label where it was dropped");
            Assert.That(design["TextBox1"], Is.EqualTo(new LayoutBox(120, 48, 100, 23)), "the TextBox where it was dropped");
            Assert.That(design["Button1"], Is.EqualTo(new LayoutBox(120, 96, 75, 23)), "the Button where it was dropped");
            Assert.That(wide["Label1"], Is.EqualTo(design["Label1"]), "Top,Left: stays put");
            Assert.That(wide["TextBox1"], Is.EqualTo(new LayoutBox(120, 48, 100 + WideW - DesignW, 23)), "Top,Left,Right: stretches");
            Assert.That(wide["Button1"], Is.EqualTo(new LayoutBox(120 + WideW - DesignW, 96, 75, 23)), "Top,Right: follows the right edge");
        });
    }

    /// <summary>
    /// Task 14 review: an executable that does not exist makes <c>Process.Start</c> THROW (Win32Exception), so the
    /// old "not recognized" check never saw it and every node-running test ERRORED where node was absent. Now it
    /// skips. (Fast: nothing is started.)
    /// </summary>
    [Test]
    public void RunPageUnderNode_WithoutNode_SkipsTheTest_RatherThanErroring()
    {
        File.WriteAllText(Path_("App.js"), "console.log('never run');\n");
        var missing = "bl-no-such-node-" + Guid.NewGuid().ToString("N");

        Exception? thrown = null;
        try
        {
            RunPageUnderNode(_dir, node: missing);
        }
        catch (Exception e)
        {
            thrown = e;
        }

        Assert.That(thrown, Is.InstanceOf<IgnoreException>(), $"a missing node must skip; got: {thrown}");
        Assert.That(thrown!.Message, Does.Contain(missing));
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
