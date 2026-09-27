using System.Text;
using System.Text.Json;
using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 10, owner decision 2026-09-27: a Canvas page RE-DOCKS when user code shows or hides a docked control at run
/// time, as WinForms does — in both states. The page carries a small reflow script whose resolver MIRRORS
/// <see cref="FormDockLayout"/> (Runtime mode) and <see cref="FormAnchorCss.Docked"/>: a mirrored pair across two
/// languages. ⛔ These tests are its lock-step gate — the SAME fixtures go through both, under node, and must agree
/// byte for byte — plus the page's OWN script, extracted from the emitted HTML and run against a stub DOM.
/// </summary>
[TestFixture]
public class FormDockScriptTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-dockscript-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    // ==================================================================
    // Fixtures — situations, not kinds: every docking rule FormDockLayoutTests pins, plus nesting and hiding
    // ==================================================================

    private static FormControl Strip(string kind, string id, string? dock = null)
    {
        var strip = new FormControl { Kind = kind, Id = id };
        if (dock != null)
        {
            strip.Properties["Dock"] = dock;
        }

        return strip;
    }

    private static FormControl Box(string id, int width, int height, string? dock, params FormControl[] children)
    {
        var box = new FormControl
        {
            Kind = "Panel", Id = id,
            Geometry = new PixelGeometry { X = 3, Y = 5, Width = width, Height = height, Dock = dock }
        };
        box.Children.AddRange(children);
        return box;
    }

    private static FormControl Hidden(FormControl control)
    {
        control.Properties["Visible"] = "False";
        return control;
    }

    private static FormDocument Page(params FormControl[] controls) => Page(400, 300, "600", controls);

    private static FormDocument Page(int width, int height, string breakpoint, params FormControl[] controls)
    {
        var page = new FormDocument
        {
            Target = FormTarget.Web, Name = "Page", Width = width, Height = height,
            Layout = new FormLayout { Kind = FormLayoutKind.Canvas, MobileBreakpoint = breakpoint }
        };
        page.Controls.AddRange(controls);
        return page;
    }

    private static readonly (string Name, Func<FormDocument> Build)[] Fixtures =
    {
        ("Top alone", () => Page(Box("a", 80, 50, "Top"))),
        ("Bottom alone", () => Page(Box("a", 80, 50, "Bottom"))),
        ("Left alone", () => Page(Box("a", 80, 50, "Left"))),
        ("Right alone", () => Page(Box("a", 80, 50, "Right"))),
        ("Fill alone", () => Page(Box("a", 80, 50, "Fill"))),
        ("strips stack", () => Page(Strip("MenuStrip", "m"), Strip("ToolStrip", "t"), Strip("StatusStrip", "s"),
            Strip("ToolStrip", "tb", "Bottom"))),
        ("a Top panel before the menu", () => Page(Box("p", 10, 40, "Top"), Strip("MenuStrip", "m"), Strip("StatusStrip", "s"),
            Box("f", 1, 1, "Fill"))),
        ("a Fill before a later Bottom overlaps it", () => Page(Box("f", 1, 1, "Fill"), Strip("StatusStrip", "s"))),
        ("Left then Top", () => Page(Box("l", 80, 10, "Left"), Box("t", 10, 40, "Top"), Box("r", 30, 10, "Right"))),
        ("Fill does not consume", () => Page(Box("f", 1, 1, "Fill"), Box("t", 10, 40, "Top"))),
        ("overflowing Top then Bottom", () => Page(Box("t", 10, 400, "Top"), Box("b", 10, 50, "Bottom"))),
        ("overflowing Left then Right", () => Page(Box("l", 500, 10, "Left"), Box("r", 50, 10, "Right"))),
        ("Fill after an overflow", () => Page(Box("t", 10, 400, "Top"), Box("f", 1, 1, "Fill"))),
        ("nested in a docked Fill", () => Page(Strip("MenuStrip", "m"),
            Box("f", 7, 7, "Fill", Strip("MenuStrip", "im"), Box("ib", 60, 20, "Bottom"), Box("il", 40, 5, "Left")))),
        ("docked inside an undocked panel", () => Page(Box("p", 200, 100, null, Box("c", 10, 30, "Top"), Box("d", 10, 10, "Fill")))),
        ("zero and negative sizes", () => Page(Box("z", 0, -5, "Top"), Box("n", -10, 20, "Left"), Box("f", 1, 1, "Fill"))),
        ("a hidden container", () => Page(Hidden(Box("p", 200, 100, "Top", Box("c", 10, 30, "Top"))), Box("f", 1, 1, "Fill")))
    };

    /// <summary>Every fixture as designed, then once per docked control (at any depth) with THAT control hidden.</summary>
    private static List<(string Name, FormDocument Document)> Cases()
    {
        var cases = new List<(string, FormDocument)>();
        foreach (var (name, build) in Fixtures)
        {
            cases.Add((name, build()));
            foreach (var id in build().AllControls().Where(c => FormDockLayout.EdgeOf(c) != null).Select(c => c.Id))
            {
                var document = build();
                document.FindById(id)!.Properties["Visible"] = "False";
                cases.Add(($"{name} / {id} hidden", document));
            }
        }

        return cases;
    }

    // ==================================================================
    // node
    // ==================================================================

    /// <summary>Runs <paramref name="script"/> under node and returns stdout; null when node is not on PATH.</summary>
    internal static string? RunNode(string dir, string fileName, string script)
    {
        File.WriteAllText(Path.Combine(dir, fileName), script);
        try
        {
            var (exit, stdout, stderr) = CliTestHarness.RunProcess("node", new[] { fileName }, dir, timeoutMs: 30_000);
            if (exit != 0 && (stderr.Contains("not recognized") || stderr.Contains("not found")))
            {
                return null;
            }

            Assert.That(exit, Is.Zero, $"node failed:\n{stdout}\n{stderr}");
            return stdout;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>What the page's live dock stylesheet must hold for <paramref name="dock"/> — composed from the C# deciders.</summary>
    internal static string LiveCss(FormDockLayoutResult dock, int breakpoint)
    {
        var rules = string.Concat(dock.All.Select(d =>
            "#" + d.Control.Id + "{" +
            string.Concat(FormAnchorCss.Docked(d).Select(p => p.Property + ":" + p.Value + ";")) + "}"));
        return breakpoint > 0 ? "@media (width >= " + breakpoint + "px){" + rules + "}" : rules;
    }

    internal sealed record GlueRun(string Options, string Before, string Flipped, string Restored);

    /// <summary>
    /// Runs the page's OWN reflow script — extracted from <paramref name="html"/> — against a stub DOM: every control
    /// of <paramref name="model"/> is an element (hidden when designed hidden), the observer is captured, and
    /// <paramref name="toggleId"/>'s visibility is flipped and then restored, the observer firing after each.
    /// Null when node is absent.
    /// </summary>
    internal static GlueRun? RunGlue(string dir, string html, FormDocument model, string toggleId)
    {
        const string open = "<script data-vgs=\"dock\">";
        var start = html.IndexOf(open, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), "the page carries no reflow script");
        start += open.Length;
        var script = html.Substring(start, html.IndexOf("</script>", start, StringComparison.Ordinal) - start);

        var elements = string.Join("\n", model.AllControls().Select(c =>
            $"el({JsonSerializer.Serialize(c.Id)}, {(c.IsHidden ? "true" : "false")});"));
        var id = JsonSerializer.Serialize(toggleId);
        var flip = model.FindById(toggleId)!.IsHidden ? "\"block\"" : "\"none\"";

        var harness = $$"""
            const els = new Map();
            function el(id, designHidden) { els.set(id, { id: id, style: {}, hidden: false, designHidden: designHidden }); }
            {{elements}}
            const form = { className: "vgs-form" };
            let observer = null, options = null, target = null;
            globalThis.MutationObserver = function (callback) {
              observer = callback;
              this.observe = function (t, o) { target = t; options = o; };
            };
            globalThis.getComputedStyle = function (e) {
              return { display: e.style.display ? e.style.display : (e.hidden || e.designHidden ? "none" : "block") };
            };
            const head = { children: [], appendChild: function (c) { this.children.push(c); return c; } };
            globalThis.document = {
              head: head,
              querySelector: function (s) { return s === ".vgs-form" ? form : null; },
              getElementById: function (id) { return els.get(id) || null; },
              createElement: function (t) { return { tagName: t, id: "", textContent: "" }; }
            };
            function live() { return head.children.length ? head.children[0].textContent : "(none)"; }
            {{script}}
            console.log("OPTIONS " + JSON.stringify({ observedForm: target === form, options: options }));
            console.log("BEFORE " + live());
            els.get({{id}}).style.display = {{flip}}; observer([]);
            console.log("FLIPPED " + live());
            els.get({{id}}).style.display = ""; observer([]);
            console.log("RESTORED " + live());
            """;

        var stdout = RunNode(dir, "glue.cjs", harness);
        if (stdout == null)
        {
            return null;
        }

        string Line(string prefix) =>
            stdout.Split('\n').Select(l => l.TrimEnd('\r')).Single(l => l.StartsWith(prefix + " ", StringComparison.Ordinal))
                .Substring(prefix.Length + 1);

        return new GlueRun(Line("OPTIONS"), Line("BEFORE"), Line("FLIPPED"), Line("RESTORED"));
    }

    // ==================================================================
    // The data and the markup (fast)
    // ==================================================================

    private static IEnumerable<string> DockedIdsIn(JsonElement node)
    {
        foreach (var child in node.GetProperty("k").EnumerateArray())
        {
            if (child.GetProperty("e").ValueKind == JsonValueKind.String)
            {
                yield return child.GetProperty("i").GetString()!;
            }

            foreach (var nested in DockedIdsIn(child))
            {
                yield return nested;
            }
        }
    }

    [Test]
    public void TheData_NamesExactlyWhatTheResolverDocks()
    {
        Assert.Multiple(() =>
        {
            foreach (var (name, document) in Cases())
            {
                using var data = JsonDocument.Parse(FormDockScript.DataJson(document));
                Assert.That(DockedIdsIn(data.RootElement),
                    Is.EquivalentTo(FormDockLayout.Resolve(document, FormDockMode.Designer).All.Select(d => d.Control.Id)),
                    name + ": the script must be able to dock everything that COULD dock (Designer), whatever is hidden now");
            }
        });
    }

    [Test]
    public void APageWithNothingDocked_CarriesNoScript()
    {
        var page = Page(Box("b", 10, 10, null));

        Assert.Multiple(() =>
        {
            Assert.That(FormDockScript.PageScript(page), Is.Null);
            Assert.That(FormAssetEmitter.Html(page, "App.js"), Does.Not.Contain("data-vgs=\"dock\""));
        });
    }

    [Test]
    public void AGridPage_NeverCarriesTheScript()
    {
        var page = new FormDocument { Target = FormTarget.Web, Name = "G", Layout = new FormLayout { Kind = FormLayoutKind.Grid } };
        page.Controls.Add(Strip("MenuStrip", "m"));

        Assert.That(FormAssetEmitter.Html(page, "App.js"), Does.Not.Contain("data-vgs=\"dock\""));
    }

    [Test]
    public void ADockedPage_CarriesAClassicScript_AfterTheFormArea_BeforeTheModule()
    {
        var html = FormAssetEmitter.Html(Page(Strip("MenuStrip", "m")), "App.js");
        var dock = html.IndexOf("<script data-vgs=\"dock\">", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(dock, Is.GreaterThan(html.LastIndexOf("</div>", StringComparison.Ordinal)),
                "after the form area: the elements it observes exist");
            Assert.That(dock, Is.LessThan(html.IndexOf("<script type=\"module\"", StringComparison.Ordinal)),
                "before the module: it is observing when InitializeComponent runs");
        });
    }

    // ==================================================================
    // The lock-step gate and the page's own script (node)
    // ==================================================================

    [Test]
    [Category("Integration")]
    public void TheScriptsResolver_AgreesWithFormDockLayout_OnEveryFixture()
    {
        var cases = Cases();
        Assert.That(cases.Any(c =>
                FormDockLayout.Resolve(c.Document, FormDockMode.Runtime).All.Count !=
                FormDockLayout.Resolve(c.Document, FormDockMode.Designer).All.Count),
            Is.True, "non-vacuity: some case hides a docked control");

        var js = new StringBuilder(FormDockScript.Core);
        js.Append("\nconst cases = [\n");
        foreach (var (_, document) in cases)
        {
            js.Append(FormDockScript.DataJson(document)).Append(",\n");
        }

        js.Append("""
            ];
            process.stdout.write(JSON.stringify(cases.map(function (root) {
              var out = [];
              vgsDockWalk(root.k, root.w, root.h, function (n) { return n.hd; }, out);
              return out.map(function (d) { return { i: d.n.i, e: d.e, b: d.b, css: vgsDockCss(d) }; });
            })));
            """);

        var stdout = RunNode(_dir, "lockstep.cjs", js.ToString());
        if (stdout == null)
        {
            Assert.Ignore("node is not on PATH, so the page's reflow script cannot be run here");
        }

        using var actual = JsonDocument.Parse(stdout!);
        Assert.That(actual.RootElement.GetArrayLength(), Is.EqualTo(cases.Count));

        Assert.Multiple(() =>
        {
            for (var k = 0; k < cases.Count; k++)
            {
                var expected = JsonSerializer.Serialize(
                    FormDockLayout.Resolve(cases[k].Document, FormDockMode.Runtime).All.Select(d => new
                    {
                        i = d.Control.Id,
                        e = d.Edge.ToString(),
                        b = new[] { d.Bounds.X, d.Bounds.Y, d.Bounds.Width, d.Bounds.Height },
                        css = FormAnchorCss.Docked(d).Select(p => new[] { p.Property, p.Value })
                    }));
                Assert.That(actual.RootElement[k].GetRawText(), Is.EqualTo(expected), cases[k].Name);
            }
        });
    }

    private static FormDocument Glued(bool panelHidden = false, string breakpoint = "600")
    {
        var panel = Box("pnlTop", 10, 40, "Top");
        if (panelHidden)
        {
            Hidden(panel);
        }

        return Page(640, 480, breakpoint, panel, Strip("MenuStrip", "menuStrip1"), Strip("StatusStrip", "statusStrip1"),
            Box("fill", 1, 1, "Fill"));
    }

    [Test]
    [Category("Integration")]
    public void HidingADockedPanel_AtRunTime_ReDocksItsSiblings_AsWinFormsDoes()
    {
        var model = Glued();
        var run = RunGlue(_dir, FormAssetEmitter.Html(model, "App.js"), model, "pnlTop");
        if (run == null)
        {
            Assert.Ignore("node is not on PATH, so the page's reflow script cannot be run here");
        }

        Assert.Multiple(() =>
        {
            Assert.That(run!.Options, Does.Contain("\"observedForm\":true"));
            Assert.That(run.Options, Does.Contain("\"subtree\":true").And.Contain("\"style\"")
                .And.Contain("\"hidden\"").And.Contain("\"class\""));
            Assert.That(run.Before, Is.EqualTo("(none)"),
                "the page's first state is its stylesheet (Runtime); the script runs only on a change");
            Assert.That(run.Flipped, Is.EqualTo(LiveCss(FormDockLayout.Resolve(Glued(panelHidden: true), FormDockMode.Runtime), 600)));
            Assert.That(run.Flipped, Does.Contain("#menuStrip1{left:0px;right:0px;top:0px;height:24px;}"),
                "non-vacuity: the menu closes the 40px gap");
            Assert.That(run.Restored, Is.EqualTo(LiveCss(FormDockLayout.Resolve(model, FormDockMode.Runtime), 600)));
            Assert.That(run.Restored, Does.Contain("#menuStrip1{left:0px;right:0px;top:40px;height:24px;}"));
        });
    }

    [Test]
    [Category("Integration")]
    public void ShowingADesignHiddenPanel_AtRunTime_MakesRoomForIt()
    {
        var model = Glued(panelHidden: true);
        var run = RunGlue(_dir, FormAssetEmitter.Html(model, "App.js"), model, "pnlTop");
        if (run == null)
        {
            Assert.Ignore("node is not on PATH, so the page's reflow script cannot be run here");
        }

        Assert.Multiple(() =>
        {
            Assert.That(run!.Flipped, Is.EqualTo(LiveCss(FormDockLayout.Resolve(Glued(), FormDockMode.Runtime), 600)),
                "shown: the siblings move down exactly as WinForms re-docks them");
            Assert.That(run.Restored, Is.EqualTo(LiveCss(FormDockLayout.Resolve(model, FormDockMode.Runtime), 600)));
        });
    }

    [Test]
    [Category("Integration")]
    public void WithABreakpointOfZero_TheLiveRulesAreUnwrapped()
    {
        var model = Glued(breakpoint: "0");
        var run = RunGlue(_dir, FormAssetEmitter.Html(model, "App.js"), model, "pnlTop");
        if (run == null)
        {
            Assert.Ignore("node is not on PATH, so the page's reflow script cannot be run here");
        }

        Assert.That(run!.Flipped, Is.EqualTo(LiveCss(FormDockLayout.Resolve(Glued(panelHidden: true, breakpoint: "0"), FormDockMode.Runtime), 0)));
    }
}
