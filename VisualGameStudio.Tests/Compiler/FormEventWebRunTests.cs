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

        var write = RegionWriter.Write(form.Name + ".bas", code, form, form.Name + ".blwebform");
        Assert.That(write.Refused, Is.False, string.Join("; ", write.Diagnostics.Select(d => d.Format())));

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

        var output = Path.Combine(_dir, "bin", "Debug", "net8.0");
        File.Copy(Path.Combine(output, "App.js"), Path.Combine(output, "app.mjs"), overwrite: true);
        File.WriteAllText(Path.Combine(output, "harness.mjs"), Harness(form) + dispatch);

        try
        {
            var (nodeExit, nodeOut, nodeErr) = CliTestHarness.RunProcess(
                "node", new[] { "harness.mjs" }, output, timeoutMs: 30_000);
            Assert.That(nodeExit, Is.Zero, $"node failed:\n{nodeOut}\n{nodeErr}\n--- {form.Name}.bas:\n{write.Text}");
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
    private static string Harness(FormDocument form)
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
}
