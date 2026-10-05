using System.Text;
using System.Text.Json;
using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Property-grid slice 5 (pre-flight Task 4, review fixes 1 and 3; ADR 0021): a page whose wiring the designer wrote —
/// <c>RegionWriter</c> over a real code-behind — built by the REAL CLI on the PROJECT route (<c>dom-core.bli</c>, the D7
/// dispatch) and RUN under node with a stub DOM, every handler printing its own name.
///
/// <para>⛔ A green build is not a running page (CLAUDE.md). The text tests in <c>FormEventEmissionTests</c> pin what is
/// emitted; only running it says the wrappers filter, the Load call runs, and the listeners fire in WinForms' order.</para>
///
/// <para>⚠ node has no <c>MouseEvent</c>/<c>KeyboardEvent</c> outside a browser, so this tier dispatches plain
/// <c>Event</c>s of <see cref="FormEvents.ListenType"/>'s type with <c>key</c>/<c>relatedTarget</c> assigned — it proves
/// wiring BY NAME and the filters' logic. A stub element knows its children for <c>contains</c>; bubbling to
/// <c>document.body</c> is emulated after each dispatch (node's EventTargets have no parents).</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class FormEventWebRunTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-formevents-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    // ==================================================================
    // The rig
    // ==================================================================

    internal static FormControl Web(string kind, string id, params (string Event, string Handler)[] binds)
    {
        var control = new FormControl { Kind = kind, Id = id, Geometry = new GridGeometry() };
        foreach (var (evt, handler) in binds)
        {
            control.Binds.Add(new FormBind { Event = evt, Handler = handler });
        }

        return control;
    }

    internal static FormDocument WebForm(string name, params FormControl[] controls)
    {
        var form = new FormDocument
        {
            Target = FormTarget.Web, Name = name,
            Layout = new FormLayout { Kind = FormLayoutKind.Grid, Cols = "auto", Rows = "auto", Gap = "8px" }
        };
        form.Controls.AddRange(controls);
        return form;
    }

    /// <summary>A handler that prints its name (and the key, when there is one) — the shape for its event.</summary>
    internal static string PrintingHandler(string name, bool takesEvent) => takesEvent
        ? $"    Private Sub {name}(e As DomEvent)\n        Console.WriteLine(\"{name}\")\n    End Sub\n"
        : $"    Private Sub {name}()\n        Console.WriteLine(\"{name}\")\n    End Sub\n";

    /// <summary>
    /// Writes the form's document and its code-behind (handlers ABOVE the init region, then <c>RegionWriter</c>), builds
    /// the project through the CLI, runs <paramref name="dispatch"/> under node after the page constructs the form, and
    /// returns node's stdout lines. Ignores (never fails) when node is absent.
    /// </summary>
    private List<string> BuildAndRun(FormDocument form, string handlers, string dispatch)
    {
        var code = FormScaffolder.Create(form.Name, FormTarget.Web).CodeText;
        var anchor = code.IndexOf("    ' Your event handlers go here", StringComparison.Ordinal);
        Assert.That(anchor, Is.GreaterThan(0), "the web scaffold's handler comment moved");
        code = code[..anchor] + handlers + code[anchor..];

        return RunNode(form, Build(form, code), dispatch);
    }

    /// <summary>
    /// Binds EVERY event in <paramref name="events"/> the way the designer does — <see cref="FormHandlers.Plan"/> writes
    /// the stub in <see cref="FormHandlers.Shape"/>'s signature, <see cref="FormHandlers.EnsureBind"/> wires it — and
    /// fills each stub's empty body with a line printing the handler's name. Returns the code-behind.
    /// </summary>
    internal static string PlannedCode(FormDocument form, IEnumerable<(FormBindOwner Owner, FormEventDef Event)> events)
    {
        var code = FormScaffolder.Create(form.Name, FormTarget.Web).CodeText;
        foreach (var (owner, evt) in events)
        {
            var plan = FormHandlers.Plan(form, owner, evt, code);
            Assert.That(plan.Outcome, Is.EqualTo(HandlerOutcome.Created), $"{owner.Label}.{evt.Name}: {plan.Refusal}");
            FormHandlers.EnsureBind(owner, plan.EventName, plan.Handler);

            var signature = $"Private Sub {plan.Handler}{FormHandlers.Shape(owner, evt, FormTarget.Web).ParameterList}\n        \n";
            Assert.That(plan.CodeText, Does.Contain(signature), "the planner's stub shape changed");
            code = plan.CodeText.Replace(signature,
                $"Private Sub {plan.Handler}{FormHandlers.Shape(owner, evt, FormTarget.Web).ParameterList}\n        Console.WriteLine(\"{plan.Handler}\")\n");
        }

        return code;
    }

    /// <summary>
    /// Writes the form's document and the region-written code-behind into a JavaScript project, builds it with the real
    /// CLI on the PROJECT route, and returns the output directory.
    /// </summary>
    private string Build(FormDocument form, string code)
    {
        var write = RegionWriter.Write(form.Name + ".bas", code, form, form.Name + ".blwebform");
        Assert.That(write.Refused, Is.False, string.Join("; ", write.Diagnostics.Select(d => d.Format())));
        _lastCode = write.Text;

        File.WriteAllText(Path.Combine(_dir, form.Name + ".bas"), write.Text);
        File.WriteAllText(Path.Combine(_dir, form.Name + ".blwebform"), FormDocumentWriter.Create(form));
        File.WriteAllText(Path.Combine(_dir, "Main.bas"), "Sub Main()\n    Console.WriteLine(\"main\")\nEnd Sub\n");
        File.WriteAllText(Path.Combine(_dir, "App.blproj"), """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>JavaScript</TargetBackend>
              </PropertyGroup>
            </BasicLangProject>
            """);

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", Path.Combine(_dir, "App.blproj") }, _dir, timeoutMs: 120_000);
        Assert.That(exit, Is.Zero, $"the CLI build failed:\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}\n--- {form.Name}.bas:\n{write.Text}");


        return Path.Combine(_dir, "bin", "Debug", "net8.0");
    }

    private string _lastCode = "";

    /// <summary>Runs <paramref name="dispatch"/> under node after the built page constructs the form; node's stdout lines.</summary>
    private List<string> RunNode(FormDocument form, string output, string dispatch)
    {
        File.Copy(Path.Combine(output, "App.js"), Path.Combine(output, "app.mjs"), overwrite: true);
        File.WriteAllText(Path.Combine(output, "harness.mjs"), Harness(form) + dispatch);

        try
        {
            var (nodeExit, nodeOut, nodeErr) = CliTestHarness.RunProcess(
                "node", new[] { "harness.mjs" }, output, timeoutMs: 30_000);
            Assert.That(nodeExit, Is.Zero, $"node failed:\n{nodeOut}\n{nodeErr}\n--- {form.Name}.bas:\n{_lastCode}");
            return nodeOut.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.Ignore("node is not on PATH — the web run tier needs it");
            throw;
        }
    }

    /// <summary>
    /// The stub DOM: <c>getElementById</c> hands out <c>EventTarget</c>s that know their children (from the form's own
    /// tree), <c>document.body</c> carries <c>data-form</c> for the D7 dispatch, <c>window</c> is an EventTarget. The
    /// dispatch helpers emulate bubbling to <c>body</c> after the target.
    /// </summary>
    internal static string Harness(FormDocument form)
    {
        var tree = new StringBuilder();
        foreach (var parent in form.AllControls().Where(c => c.Children.Count > 0))
        {
            foreach (var child in parent.Children)
            {
                tree.Append($"get({JsonSerializer.Serialize(parent.Id)}).kids.add(get({JsonSerializer.Serialize(child.Id)}));\n");
            }
        }

        return $$"""
            class El extends EventTarget {
              constructor(id) { super(); this.id = id; this.kids = new Set(); }
              contains(x) { return x === this || [...this.kids].some(k => k.contains(x)); }
              getAttribute(n) { return n === "data-form" ? {{JsonSerializer.Serialize(form.Name)}} : null; }
            }
            const els = {};
            const get = id => (els[id] ??= new El(id));
            const body = new El("body");
            globalThis.document = { body, getElementById: get };
            globalThis.window = new El("window");
            {{tree}}
            await import("./app.mjs");
            console.log("--- constructed");
            function dispatch(target, type, props) {
              const ev = new Event(type, { bubbles: true });
              Object.assign(ev, props ?? {});
              target.dispatchEvent(ev);
              if (target !== body && target !== window) {
                const up = new Event(type, { bubbles: true });
                Object.assign(up, props ?? {});
                body.dispatchEvent(up);
              }
            }
            const key = (id, k) => dispatch(id === "body" ? body : get(id), "keydown", { key: k });
            const focus = (id, type, related) => dispatch(get(id), type, { relatedTarget: related ? get(related) : null });

            """;
    }

    private static List<string> After(List<string> lines, string marker = "--- constructed") =>
        lines.SkipWhile(l => l != marker).Skip(1).ToList();

    // ==================================================================
    // Review fix 1 — WinForms' raise order: KeyDown before KeyPress
    // ==================================================================

    /// <summary>
    /// Binds stored KeyPress FIRST, KeyDown second, on a TextBox and on the Form: for each key the page must still run
    /// KeyDown before KeyPress — WinForms always does, and suppressing a KeyPress from KeyDown relies on it.
    /// </summary>
    [Test]
    public void KeyDown_RunsBeforeKeyPress_WhenTheBindsAreStoredInReverse()
    {
        var form = WebForm("KeyForm", Web("TextBox", "txt", ("keypress", "txt_KeyPress"), ("keydown", "txt_KeyDown")));
        form.Binds.Add(new FormBind { Event = "keypress", Handler = "KeyForm_KeyPress" });
        form.Binds.Add(new FormBind { Event = "keydown", Handler = "KeyForm_KeyDown" });

        var lines = BuildAndRun(form,
            PrintingHandler("txt_KeyPress", true) + PrintingHandler("txt_KeyDown", true) +
            PrintingHandler("KeyForm_KeyPress", true) + PrintingHandler("KeyForm_KeyDown", true),
            "key(\"txt\", \"a\");\n");

        Assert.That(After(lines), Is.EqualTo(new[] { "txt_KeyDown", "txt_KeyPress", "KeyForm_KeyDown", "KeyForm_KeyPress" }));
    }

    // ==================================================================
    // Review fix 3 — an astral key is ONE character
    // ==================================================================

    [Test]
    public void AnAstralCharacterKey_RunsKeyPressOnce_AndANamedKeyDoesNot()
    {
        var form = WebForm("EmojiForm", Web("TextBox", "txt", ("keypress", "txt_KeyPress")));
        var smile = char.ConvertFromUtf32(0x1F600);

        var lines = BuildAndRun(form, PrintingHandler("txt_KeyPress", true),
            $"key(\"txt\", {JsonSerializer.Serialize(smile)});\nconsole.log(\"--- named\");\nkey(\"txt\", \"F1\");\n");

        Assert.Multiple(() =>
        {
            Assert.That(After(lines).TakeWhile(l => l != "--- named"), Is.EqualTo(new[] { "txt_KeyPress" }),
                "U+1F600 is two UTF-16 units and ONE character: WinForms raises KeyPress for it");
            Assert.That(After(lines, "--- named"), Is.Empty, "F1 is two characters and no key WinForms raises KeyPress for");
        });
    }

    /// <summary>
    /// Review ruling 6: a keydown fired while an IME composes (<c>isComposing</c>) raises no KeyPress — the composed text is
    /// committed by the IME, not typed key by key; and a control named <c>vgsKey</c>… is not one — but one named <c>k</c> or
    /// <c>n</c> (the old wrapper locals) must still work, so the page uses <c>k</c> here.
    /// </summary>
    [Test]
    public void AComposingKeydown_RaisesNoKeyPress_AndAControlNamedK_IsNotShadowed()
    {
        var form = WebForm("ImeForm", Web("TextBox", "k", ("keypress", "k_KeyPress")), Web("TextBox", "n"));

        var lines = BuildAndRun(form, PrintingHandler("k_KeyPress", true),
            "dispatch(get(\"k\"), \"keydown\", { key: \"a\", isComposing: true });\nconsole.log(\"--- plain\");\n" +
            "dispatch(get(\"k\"), \"keydown\", { key: \"a\", isComposing: false });\nkey(\"k\", \"b\");\n");

        Assert.Multiple(() =>
        {
            Assert.That(After(lines).TakeWhile(l => l != "--- plain"), Is.Empty, "composing: no KeyPress");
            Assert.That(After(lines, "--- plain"), Is.EqualTo(new[] { "k_KeyPress", "k_KeyPress" }),
                "isComposing false, and absent (the harness's plain key), both raise it");
        });
    }

    // ==================================================================
    // Task 4 — every web event of every web kind, and the Form, RUN
    // ==================================================================

    /// <summary>
    /// Every catalog kind that is an ELEMENT on the page and wires at least one event there. A tray component is not
    /// here: its only web event is a Timer's <c>setInterval</c> tick, never a dispatched DOM event (and an interval would
    /// keep node alive) — <c>FormComponentAcceptanceTests</c> runs it.
    /// </summary>
    private static IEnumerable<TestCaseData> EveryWebElementKind() =>
        FormControlCatalog.All
            .Where(d => d.SupportsTarget(FormTarget.Web) && !d.IsComponent && FormEvents.WiredOn(d, FormTarget.Web).Count > 0)
            .Select(d => new TestCaseData(d.Kind).SetName("{m}(" + d.Kind + ")"));

    [Test]
    public void TheKindSweep_HasSomethingToSweep() =>
        Assert.That(EveryWebElementKind().Count(), Is.GreaterThanOrEqualTo(20));

    private static readonly string[] KeyPressKeys = { "Enter", "Backspace", "Escape" };
    private static readonly string[] NotKeyPressKeys = { "Shift", "ArrowLeft", "F1" };

    /// <summary>
    /// One control of <paramref name="kind"/> with EVERY web event bound through the designer's own planner; each DOM type
    /// <see cref="FormEvents.ListenType"/> names is dispatched ONCE (with a <c>key</c> and a <c>relatedTarget</c> outside
    /// the element) and every handler must run exactly once — wiring BY NAME, through the CLI-built page. Then the filters:
    /// KeyPress runs for Enter/Backspace/Escape and never for Shift/ArrowLeft/F1 while KeyDown runs for all six; a focus
    /// move from one child of the element to another raises neither Enter nor Leave, and a <c>null</c> relatedTarget does.
    /// </summary>
    [TestCaseSource(nameof(EveryWebElementKind))]
    public void EveryWebEvent_OfTheKind_RunsItsHandlerOnce_AndTheFiltersHold(string kind)
    {
        var form = WebForm("SweepPage");
        var definition = FormControlCatalog.Find(kind)!;
        var control = FormCatalogShapes.Canonical(form, definition, "ctl");
        var owner = new FormBindOwner(form, control);
        var events = FormEvents.WiredOn(definition, FormTarget.Web);
        var code = PlannedCode(form, events.Select(e => (owner, e)));
        string Handler(string name) => FormHandlers.NameFor("ctl", name);

        var types = events.Select(e => FormEvents.ListenType(e)!).Distinct().ToList();
        var dispatch = new StringBuilder("get(\"ctl\").kids.add(get(\"inner1\")); get(\"ctl\").kids.add(get(\"inner2\"));\n");
        foreach (var type in types)
        {
            dispatch.Append($"dispatch(get(\"ctl\"), {JsonSerializer.Serialize(type)}, {{ key: \"a\", relatedTarget: get(\"outside\") }});\n");
        }

        var keyPress = events.FirstOrDefault(e => e.WebFilter == FormWebFilter.KeyPressKeys);
        var keyDown = events.FirstOrDefault(e => e.Name == "KeyDown");
        var focus = events.Where(e => e.WebFilter == FormWebFilter.FromOutside).ToList();
        dispatch.Append("console.log(\"--- keys\");\n");
        foreach (var k in KeyPressKeys.Concat(NotKeyPressKeys).Where(_ => keyPress != null || keyDown != null))
        {
            dispatch.Append($"key(\"ctl\", {JsonSerializer.Serialize(k)});\n");
        }

        dispatch.Append("console.log(\"--- inside\");\n");
        foreach (var e in focus)
        {
            dispatch.Append($"focus(\"ctl\", {JsonSerializer.Serialize(e.WebEvent)}, \"inner1\");\n");
        }

        dispatch.Append("console.log(\"--- null\");\n");
        foreach (var e in focus)
        {
            dispatch.Append($"focus(\"ctl\", {JsonSerializer.Serialize(e.WebEvent)}, null);\n");
        }

        var lines = RunNode(form, Build(form, code), dispatch.ToString());
        var once = After(lines).TakeWhile(l => l != "--- keys").ToList();
        var keys = After(lines, "--- keys").TakeWhile(l => l != "--- inside").ToList();
        var inside = After(lines, "--- inside").TakeWhile(l => l != "--- null").ToList();
        var nulls = After(lines, "--- null").ToList();

        Assert.Multiple(() =>
        {
            Assert.That(once, Is.EquivalentTo(events.Select(e => Handler(e.Name))),
                $"each of {kind}'s {events.Count} web events, dispatched once by its listen type, runs its handler once");
            Assert.That(keys.Count(l => l == Handler("KeyPress")), Is.EqualTo(keyPress == null ? 0 : 3),
                "KeyPress: Enter, Backspace, Escape — never Shift, ArrowLeft, F1");
            Assert.That(keys.Count(l => l == Handler("KeyDown")), Is.EqualTo(keyDown == null ? 0 : 6));
            Assert.That(inside, Is.Empty, "focus moving between two children of the element raises no Enter/Leave on it");
            Assert.That(nulls, Is.EquivalentTo(focus.Select(e => Handler(e.Name))));
        });
    }

    /// <summary>
    /// The Form: Load runs ONCE, at construction, BEFORE any dispatch (a call at the end of InitializeComponent); Resize
    /// listens on the window; Click/KeyDown/KeyUp on the body; KeyPress on the body through its filter.
    /// </summary>
    [Test]
    public void TheFormsWebEvents_Run_AndLoadRunsOnceAtConstruction()
    {
        var form = WebForm("RootPage", Web("Button", "btn"));
        var owner = new FormBindOwner(form);
        var events = FormEvents.WiredOn(FormControlCatalog.FormRoot, FormTarget.Web);
        var code = PlannedCode(form, events.Select(e => (owner, e)));

        var lines = RunNode(form, Build(form, code),
            "console.log(\"--- dispatch\");\n" +
            "dispatch(window, \"resize\");\n" +
            "dispatch(body, \"click\");\n" +
            "key(\"body\", \"a\");\n" +
            "dispatch(body, \"keyup\", { key: \"a\" });\n" +
            "console.log(\"--- keys\");\n" +
            string.Concat(KeyPressKeys.Concat(NotKeyPressKeys).Select(k => $"key(\"body\", {JsonSerializer.Serialize(k)});\n")));

        var beforeDispatch = lines.TakeWhile(l => l != "--- dispatch").ToList();
        var dispatched = After(lines, "--- dispatch").TakeWhile(l => l != "--- keys").ToList();
        var keys = After(lines, "--- keys");

        Assert.Multiple(() =>
        {
            Assert.That(beforeDispatch.Count(l => l == "RootPage_Load"), Is.EqualTo(1), "Load once, at construction");
            Assert.That(beforeDispatch.IndexOf("RootPage_Load"), Is.LessThan(beforeDispatch.IndexOf("--- constructed")));
            Assert.That(dispatched.Concat(keys), Has.No.Member("RootPage_Load"));
            Assert.That(dispatched, Is.EqualTo(new[]
            {
                "RootPage_Resize", "RootPage_Click", "RootPage_KeyDown", "RootPage_KeyPress", "RootPage_KeyUp"
            }));
            Assert.That(keys.Count(l => l == "RootPage_KeyPress"), Is.EqualTo(3));
            Assert.That(keys.Count(l => l == "RootPage_KeyDown"), Is.EqualTo(6));
        });
    }

    // ==================================================================
    // Task 4 — the Edge tier: the REAL event interfaces, a real .focus()
    // ==================================================================

    /// <summary>
    /// The page in Microsoft Edge (headless, a throw-away profile, served from the shipping loopback server — a
    /// <c>file://</c> origin stops the module script): Button Click/MouseEnter/KeyDown, TextBox KeyPress, a Panel with two
    /// TextBoxes and Enter, the Form's Load and Resize. Each event is dispatched with its REAL interface —
    /// <c>new (DomInterfaceOf(webEvent))(ListenType(evt), …)</c>, so a KeyPress is a <c>KeyboardEvent("keydown")</c> — and
    /// focus is moved with real <c>.focus()</c> calls: in from a Button outside the Panel (Enter runs once — the positive
    /// control that proves focus events fire at all here), then from one Panel child to the other (Enter must NOT run).
    /// ⚠ Synthetic dispatch, not real input — real input (CDP <c>Input.*</c>) is piece 2's Task 35. SKIPS without Edge.
    /// </summary>
    [Test]
    public void InEdge_TheRealInterfaces_RunTheHandlers_AndAChildToChildFocusMoveRaisesNoEnter()
    {
        var edge = VisualGameStudio.Tests.Compiler.PixelLayout.EdgeLayoutHarness.EdgePath();
        if (edge == null)
        {
            Assert.Ignore("Microsoft Edge is not installed (or this is not Windows) — the Edge event tier needs it");
        }

        var form = new FormDocument
        {
            Target = FormTarget.Web, Name = "EdgePage", Width = 640, Height = 480,
            Layout = new FormLayout { Kind = FormLayoutKind.Canvas, MobileBreakpoint = "0" }
        };
        FormControl At(string kind, string id, int x, int y, int tab) => new()
        {
            Kind = kind, Id = id, TabIndex = tab, Geometry = new PixelGeometry { X = x, Y = y, Width = 120, Height = 24 }
        };
        var btn = At("Button", "btn", 8, 8, 0);
        var txt = At("TextBox", "txt", 8, 40, 1);
        var outside = At("Button", "outsideBtn", 8, 72, 2);
        var pnl = new FormControl
        {
            Kind = "Panel", Id = "pnl", TabIndex = 3, Geometry = new PixelGeometry { X = 200, Y = 8, Width = 200, Height = 120 }
        };
        pnl.Children.Add(At("TextBox", "c1", 8, 8, 0));
        pnl.Children.Add(At("TextBox", "c2", 8, 40, 1));
        form.Controls.AddRange(new[] { btn, txt, outside, pnl });

        FormEventDef E(FormControlDef d, string name) => d.Events!.Single(e => e.Name == name);
        var button = FormControlCatalog.Find("Button")!;
        var root = new FormBindOwner(form);
        var binds = new List<(FormBindOwner, FormEventDef)>
        {
            (new FormBindOwner(form, btn), E(button, "Click")),
            (new FormBindOwner(form, btn), E(button, "MouseEnter")),
            (new FormBindOwner(form, btn), E(button, "KeyDown")),
            (new FormBindOwner(form, txt), E(FormControlCatalog.Find("TextBox")!, "KeyPress")),
            (new FormBindOwner(form, pnl), E(FormControlCatalog.Find("Panel")!, "Enter")),
            (root, E(FormControlCatalog.FormRoot, "Load")),
            (root, E(FormControlCatalog.FormRoot, "Resize"))
        };
        var output = Build(form, PlannedCode(form, binds));

        // Each dispatch through the gates-only table: the REAL interface, the listened-to type.
        string Send(string target, FormEventDef evt, string? key = null) =>
            $"{target}.dispatchEvent(new {FormEvents.DomInterfaceOf(evt.WebEvent!)}(" +
            $"{JsonSerializer.Serialize(FormEvents.ListenType(evt))}, {{ bubbles: true{(key == null ? "" : ", key: " + JsonSerializer.Serialize(key))} }}));\n";
        var keyPress = E(FormControlCatalog.Find("TextBox")!, "KeyPress");
        var driver =
            "await turn(); await turn();\n" +
            "mark('--- loaded');\n" +
            Send("el('btn')", E(button, "Click")) + Send("el('btn')", E(button, "MouseEnter")) + Send("el('btn')", E(button, "KeyDown"), "a") +
            "mark('--- keys');\n" +
            Send("el('txt')", keyPress, "a") + Send("el('txt')", keyPress, "Shift") +
            $"el('txt').dispatchEvent(new KeyboardEvent('keydown', {{ bubbles: true, key: String.fromCodePoint(0x1F600) }}));\n" +
            Send("el('txt')", keyPress, "Enter") + Send("el('txt')", keyPress, "ArrowLeft") +
            "mark('--- focus in');\n" +
            "el('outsideBtn').focus(); await turn(); el('c1').focus(); await turn();\n" +
            "mark('--- focus between');\n" +
            "el('c2').focus(); await turn();\n" +
            "mark('--- resize');\n" +
            "window.dispatchEvent(new Event('resize'));\n";

        var log = RunInEdge(edge!, output, form.Name, driver);
        List<string> Between(string from, string? to) =>
            log.SkipWhile(l => l != from).Skip(1).TakeWhile(l => to == null || l != to).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(log.TakeWhile(l => l != "--- loaded").Count(l => l == "EdgePage_Load"), Is.EqualTo(1), string.Join("\n", log));
            Assert.That(Between("--- loaded", "--- keys"), Is.EqualTo(new[] { "btn_Click", "btn_MouseEnter", "btn_KeyDown" }));
            Assert.That(Between("--- keys", "--- focus in"), Is.EqualTo(new[] { "txt_KeyPress", "txt_KeyPress", "txt_KeyPress" }),
                "a, U+1F600 and Enter — never Shift or ArrowLeft");
            Assert.That(Between("--- focus in", "--- focus between"), Is.EqualTo(new[] { "pnl_Enter" }),
                "real focus moving INTO the Panel raises its Enter once (the control that proves focus events fire here)");
            Assert.That(Between("--- focus between", "--- resize"), Is.Empty,
                "real focus moving between two children of the Panel raises no Enter");
            Assert.That(Between("--- resize", null), Is.EqualTo(new[] { "EdgePage_Resize" }));
            Assert.That(log.Where(l => l.StartsWith("ERROR", StringComparison.Ordinal)), Is.Empty);
        });
    }

    /// <summary>
    /// Serves <paramref name="output"/> from the loopback preview server, loads a copy of <c>&lt;form&gt;.html</c> carrying a
    /// console-capturing script before the page's module and <paramref name="driver"/> after it, and returns the captured
    /// console lines (and any page error as <c>ERROR …</c>) from Edge's <c>--dump-dom</c>.
    /// </summary>
    internal static List<string> RunInEdge(string edge, string output, string formName, string driver)
    {
        var html = File.ReadAllText(Path.Combine(output, formName + ".html"));
        var head = html.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
        var end = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        Assert.That(head >= 0 && end > head, Is.True, $"{formName}.html has no <head>/</body>");

        const string capture = """
            <script>
            window.__vgsLog = [];
            (function () {
              var original = console.log;
              console.log = function () { window.__vgsLog.push(Array.prototype.join.call(arguments, " ")); original.apply(console, arguments); };
              window.addEventListener("error", function (e) { window.__vgsLog.push("ERROR " + (e.message || e)); });
            })();
            </script>
            """;
        var run = $$"""
            <script>
            window.addEventListener("load", async function () {
              function turn() { return new Promise(function (r) { setTimeout(r, 0); }); }
              function el(id) { var e = document.getElementById(id); if (!e) throw new Error("no element " + id); return e; }
              function mark(m) { window.__vgsLog.push(m); }
              try {
            {{driver}}
              } catch (e) { window.__vgsLog.push("ERROR " + (e && e.stack ? e.stack : e)); }
              var out = document.createElement("pre");
              out.id = "vgs-out";
              out.textContent = JSON.stringify(window.__vgsLog);
              out.setAttribute("data-done", "1");
              document.body.appendChild(out);
            });
            </script>
            """;
        var page = html.Insert(end, run).Insert(head + "<head>".Length, capture);
        File.WriteAllText(Path.Combine(output, formName + ".events.html"), page);

        var (dump, _, _) = VisualGameStudio.Tests.Compiler.PixelLayout.EdgeLayoutHarness.WithThrowawayProfile(profile =>
        {
            using var server = new VisualGameStudio.ProjectSystem.Services.WebPreviewServer();
            var url = server.Start(output);
            try
            {
                var (exit, stdout, stderr) = CliTestHarness.RunProcess(edge, new[]
                {
                    "--headless=new", $"--user-data-dir={profile}", "--no-first-run", "--no-default-browser-check",
                    "--disable-extensions", "--window-size=1024,768", "--virtual-time-budget=10000", "--dump-dom",
                    url + formName + ".events.html"
                }, output, timeoutMs: 120_000);
                Assert.That(exit, Is.Zero, $"Edge exited {exit}.\n{stderr}");
                return stdout;
            }
            finally
            {
                server.Stop();
            }
        });

        var match = System.Text.RegularExpressions.Regex.Match(dump, "<pre id=\"vgs-out\"[^>]*data-done=\"1\"[^>]*>(.*?)</pre>",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.That(match.Success, Is.True, "the driver never finished before Edge dumped the page:\n" +
                                             (dump.Length > 3000 ? dump[..3000] : dump));
        return JsonSerializer.Deserialize<List<string>>(System.Net.WebUtility.HtmlDecode(match.Groups[1].Value))!;
    }
}
