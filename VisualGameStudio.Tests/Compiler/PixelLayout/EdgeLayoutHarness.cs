using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Compiler.PixelLayout;

/// <summary>
/// Something the measuring script does to the page after the first snapshot; a snapshot labelled
/// <see cref="Label"/> is taken after each (a task turn later, so the reflow script's MutationObserver has run).
/// </summary>
/// <param name="Kind"><c>display</c> (<c>el.style.display = Value</c>), <c>hidden</c> (<c>el.hidden</c>), <c>class</c>
/// (add/remove a <c>display:none !important</c> class), <c>open</c> (a menu item's dropdown, B4), <c>hit</c>
/// (<c>elementFromPoint</c> at form-client X/Y → probe), <c>text</c> (is Value in the form area's rendered text →
/// probe), <c>cost</c> (Value colour writes to an undocked control → getComputedStyle calls per write, probe).</param>
internal sealed record EdgeStep(string Label, string Kind, string Id = "", string Value = "", double X = 0, double Y = 0)
{
    public static EdgeStep Display(string label, string id, string display) => new(label, "display", id, display);
    public static EdgeStep Hidden(string label, string id, bool hidden) => new(label, "hidden", id, hidden ? "true" : "false");
    public static EdgeStep Class(string label, string id, bool add) => new(label, "class", id, add ? "add" : "remove");
    public static EdgeStep OpenMenu(string label, string itemId) => new(label, "open", itemId);
    public static EdgeStep HitTest(string label, double x, double y) => new(label, "hit", X: x, Y: y);
    public static EdgeStep HasText(string label, string text) => new(label, "text", Value: text);
    public static EdgeStep StyleCost(string label, string id, int writes) => new(label, "cost", id, writes.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Is the element <paramref name="id"/> — anywhere on the page, not only in the form area — rendered wholly inside
    /// the VIEWPORT (non-empty, within innerWidth × innerHeight, before any scrolling)? Probe: <c>true</c>, or
    /// <c>false {rect}</c> naming where it was.
    /// </summary>
    public static EdgeStep InViewport(string label, string id) => new(label, "inview", id);

    /// <summary>The element <paramref name="id"/>'s border box in VIEWPORT px, as the probe JSON <c>{"x":…,"y":…,"w":…,"h":…}</c>.</summary>
    public static EdgeStep Rect(string label, string id) => new(label, "rect", id);

    /// <summary>
    /// The COMPUTED value of CSS property <paramref name="cssProperty"/> on element <paramref name="id"/> — or on the page's
    /// &lt;body&gt; when <paramref name="id"/> is empty (slice 3: the Form's own web rows are body CSS). What the browser
    /// actually resolved, not what the stylesheet says.
    /// </summary>
    public static EdgeStep Style(string label, string id, string cssProperty) => new(label, "style", id, cssProperty);
}

/// <summary>One page loaded into an iframe of exactly <see cref="Width"/>×<see cref="Height"/> CSS px.</summary>
/// <param name="Name">Unique; the key of the result.</param>
/// <param name="Form">The page (<c>&lt;Form&gt;.html</c>) to load.</param>
/// <param name="Ids">Every control to measure — each must exist on the page.</param>
internal sealed record EdgeCase(string Name, string Form, int Width, int Height, IReadOnlyList<string> Ids, IReadOnlyList<EdgeStep> Steps)
{
    /// <summary>The case for <paramref name="document"/> at a viewport size, measuring every control the WinForms
    /// driver measures (<see cref="ReferenceFixture.Ids"/> — the one rule).</summary>
    public static EdgeCase Of(FormDocument document, int width, int height, params EdgeStep[] steps) =>
        new($"{document.Name}@{width}x{height}", document.Name, width, height, new ReferenceFixture(document).Ids, steps);
}

/// <summary>An image on the page: loaded or not, and its intrinsic size.</summary>
internal readonly record struct EdgeImage(bool Complete, double NaturalWidth, double NaturalHeight);

/// <summary>What Edge measured for one <see cref="EdgeCase"/>. Rectangles are FORM-CLIENT CSS px (spec §7 item 2).</summary>
/// <param name="FormArea">The form area's BORDER box in viewport coordinates.</param>
/// <param name="FormClientOrigin">The form area's client origin in viewport coordinates (rect + clientLeft/Top).</param>
/// <param name="ScrollWidth">The document's scroll size — ≥ the design size when the page scrolls instead of squashing.</param>
/// <param name="Literal">The <c>.vgs-literal</c> wrapper's box, or null when the page has none.</param>
internal sealed record EdgeCaseResult(
    EdgeCase Case, int ViewportWidth, int ViewportHeight, double DevicePixelRatio, string UserAgent,
    LayoutBox FormArea, (double X, double Y) FormClientOrigin, double ScrollWidth, double ScrollHeight,
    IReadOnlyList<LayoutSnapshot> Snapshots, IReadOnlyDictionary<string, string> Probes,
    IReadOnlyList<string> Errors, IReadOnlyDictionary<string, EdgeImage> Images, LayoutBox? Literal)
{
    public LayoutSnapshot this[string label] =>
        Snapshots.FirstOrDefault(s => s.Label == label)
        ?? throw new KeyNotFoundException($"{Case.Name} has no snapshot '{label}'");
}

/// <summary>One Edge run: every case's result, and whether the throw-away profile was removed afterwards.</summary>
internal sealed record EdgeRun(IReadOnlyDictionary<string, EdgeCaseResult> Results, bool ProfileDeleted, string ProfileDirectory, TimeSpan Took);

/// <summary>
/// ⛔⛔ Task 13 — Microsoft Edge headless as the page's measuring instrument (spec 2026-09-27 §7, pre-flight
/// <c>2026-09-27-web-pixel-layout-task13-preflight.md</c>). The site is built by the REAL CLI; it is SERVED from the
/// shipping loopback <see cref="WebPreviewServer"/> (a <c>file://</c> origin is opaque and stops both the module
/// script and the harness reading its iframe); one harness page loads each case in turn into ONE iframe of exact size
/// at (0,0); a CLASSIC measuring script appended to a served copy of each page measures and reports to the harness,
/// which writes every result into <c>&lt;pre id="out"&gt;</c> for <c>--dump-dom</c>.
///
/// <para>The rules, each found necessary rather than decorative:</para>
/// <list type="bullet">
///   <item><description>⛔ A fresh <c>--user-data-dir</c> per run (without it a launch can hand off to the owner's
///   running Edge and exit), deleted afterwards. The process is run through <see cref="CliTestHarness.RunProcess"/>,
///   which on timeout kills ITS OWN process tree by PID — ⛔ never by name: the owner runs Edge.</description></item>
///   <item><description><c>--force-device-scale-factor=1</c>, and the parser REFUSES a <c>devicePixelRatio</c> that
///   is not 1: CSS px are then the 96-DPI pixels the WinForms reference reports.</description></item>
///   <item><description>The iframe's size is checked: the parser refuses a viewport other than the one asked for.</description></item>
///   <item><description>A rectangle is the BORDER box (<c>getBoundingClientRect</c>) minus the form area's CLIENT
///   origin (Task 12 B8). A requested control the page lacks is REFUSED, never skipped.</description></item>
///   <item><description>Measured on <c>load</c> (images decoded) plus two task turns; after each step one task turn,
///   so the reflow script's MutationObserver microtask has run. Never <c>requestAnimationFrame</c>: a throttled
///   frame never fires it.</description></item>
/// </list>
/// </summary>
internal static class EdgeLayoutHarness
{
    /// <summary>Every Edge comparison's tolerance (spec §7): ±1 CSS px.</summary>
    public const double Tolerance = 1;

    /// <summary>Virtual milliseconds one case may take before the harness page gives up on it and NAMES it.</summary>
    public const int PerCaseTimeoutMs = 10_000;

    /// <summary>
    /// Virtual milliseconds Edge may spend before it dumps (it fast-forwards when idle). ⛔ Room for EVERY case to hit
    /// its own timeout, plus slack: a budget smaller than that would dump an unfinished run and a hung case would be
    /// reported as "did not finish" instead of by name (review M-3).
    /// </summary>
    public static int VirtualTimeBudgetMs(int cases) => (cases + 1) * PerCaseTimeoutMs;

    private static readonly string[] EdgeLocations =
    {
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
    };

    /// <summary>The installed Edge, or null (not Windows, or not installed) — the caller SKIPS with the reason.</summary>
    public static string? EdgePath() =>
        OperatingSystem.IsWindows() ? EdgeLocations.FirstOrDefault(File.Exists) : null;

    /// <summary><see cref="LayoutComparison.Differences"/> at <see cref="Tolerance"/> — the one Edge comparison.</summary>
    public static IReadOnlyList<string> Differences(
        IReadOnlyDictionary<string, LayoutBox> expected, IReadOnlyDictionary<string, LayoutBox> actual) =>
        LayoutComparison.Differences(expected, actual, Tolerance);

    // ================================================================== the site

    /// <summary>
    /// Builds ONE JavaScript project holding every document as a <c>.blwebform</c>, with the real CLI deployed next
    /// to the tests, writes <paramref name="files"/> beside the pages, and returns the output directory.
    /// </summary>
    public static string BuildSite(string workDir, IEnumerable<FormDocument> documents, IReadOnlyDictionary<string, byte[]> files)
    {
        var project = Path.Combine(workDir, "web");
        Directory.CreateDirectory(project);

        var names = new List<string>();
        foreach (var document in documents)
        {
            Assert.That(names, Does.Not.Contain(document.Name), "every page needs its own form name");
            names.Add(document.Name);
            File.WriteAllText(Path.Combine(project, document.Name + ".blwebform"), FormDocumentWriter.Create(document));
        }

        var items = string.Join("\n    ", names.Select(n => $"<Compile Include=\"{n}.blwebform\" />"));
        File.WriteAllText(Path.Combine(project, "App.blproj"), $"""
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>JavaScript</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Main.bas" />
                {items}
              </ItemGroup>
            </BasicLangProject>
            """);
        File.WriteAllText(Path.Combine(project, "Main.bas"), "Sub Main()\n    Console.WriteLine(\"App loaded\")\nEnd Sub\n");

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", Path.Combine(project, "App.blproj") }, project, timeoutMs: 180_000);
        Assert.That(exit, Is.Zero, $"the real CLI did not build the pages.\n{stdout}\n{stderr}");

        var output = Path.Combine(project, "bin", "Debug", "net8.0");
        foreach (var name in names)
        {
            Assert.That(File.Exists(Path.Combine(output, name + ".html")), Is.True, $"the build wrote no {name}.html.\n{stdout}");
        }

        foreach (var (file, bytes) in files)
        {
            File.WriteAllBytes(Path.Combine(output, file), bytes);
        }

        return output;
    }

    // ================================================================== the run

    /// <summary>
    /// Measures every case in ONE Edge run against the site in <paramref name="siteDir"/>. Skips (never passes)
    /// without Edge.
    /// </summary>
    public static EdgeRun Measure(string siteDir, IReadOnlyList<EdgeCase> cases)
    {
        var edge = EdgePath();
        if (edge == null)
        {
            TestSkip.IgnoreEvenInsideMultiple("Microsoft Edge is not installed (or this is not Windows), so the page cannot be laid out");
        }

        Assert.That(cases.Select(c => c.Name), Is.Unique, "every case needs its own name");

        File.WriteAllText(Path.Combine(siteDir, "vgs-measure.js"), MeasureScript);
        foreach (var form in cases.Select(c => c.Form).Distinct())
        {
            var html = File.ReadAllText(Path.Combine(siteDir, form + ".html"));
            var at = html.LastIndexOf("</body>", StringComparison.Ordinal);
            Assert.That(at, Is.GreaterThan(0), $"{form}.html has no </body>");
            // ⛔ CLASSIC, after the page's module tag: it registers its error listeners before the module runs.
            File.WriteAllText(Path.Combine(siteDir, form + ".measure.html"),
                html.Insert(at, "<script src=\"vgs-measure.js\"></script>\n"));
        }

        File.WriteAllText(Path.Combine(siteDir, "harness.html"), HarnessPage(cases));

        var started = DateTime.UtcNow;
        var (dump, deleted, profile) = WithThrowawayProfile(profile =>
        {
            using var server = new WebPreviewServer();
            var url = server.Start(siteDir);
            try
            {
                var (exit, stdout, stderr) = CliTestHarness.RunProcess(edge!, new[]
                {
                    "--headless=new",
                    $"--user-data-dir={profile}",
                    "--hide-scrollbars",
                    "--force-device-scale-factor=1",
                    "--no-first-run",
                    "--no-default-browser-check",
                    "--disable-extensions",
                    "--window-size=1280,1024",
                    $"--virtual-time-budget={VirtualTimeBudgetMs(cases.Count)}",
                    "--dump-dom",
                    url + "harness.html"
                }, siteDir, timeoutMs: 180_000);

                Assert.That(exit, Is.Zero, $"Edge exited {exit}.\n{stderr}");
                return stdout;
            }
            finally
            {
                server.Stop();
            }
        });

        var took = DateTime.UtcNow - started;
        File.WriteAllText(Path.Combine(siteDir, "edge-dump.html"), dump);
        return new EdgeRun(Parse(dump, cases), deleted, profile, took);
    }

    /// <summary>
    /// Runs <paramref name="run"/> with a fresh <c>bl-edge-profile-*</c> directory for Edge's
    /// <c>--user-data-dir</c> and deletes it afterwards — ⛔ in a <c>finally</c>, so a failing run (a non-zero exit, a
    /// timeout that killed Edge's tree, any exception) leaves no profile behind either (review I-1).
    /// </summary>
    internal static (T Result, bool Deleted, string Profile) WithThrowawayProfile<T>(Func<string, T> run)
    {
        var profile = Path.Combine(Path.GetTempPath(), "bl-edge-profile-" + Guid.NewGuid().ToString("N"));
        var completed = false;
        try
        {
            var result = run(profile);
            completed = true;
            return (result, DeleteProfile(profile), profile);
        }
        finally
        {
            if (!completed && !DeleteProfile(profile))
            {
                TestContext.Out.WriteLine($"[edge] a failed run could not remove its profile: {profile}");
            }
        }
    }

    /// <summary>Edge's helpers can hold the profile for a moment after the browser exits: retried, then reported.</summary>
    private static bool DeleteProfile(string profile)
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            try
            {
                if (Directory.Exists(profile))
                {
                    Directory.Delete(profile, recursive: true);
                }

                return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            Thread.Sleep(200);
        }

        return !Directory.Exists(profile);
    }

    // ================================================================== the parser

    private static readonly Regex Out = new("<pre id=\"out\"([^>]*)>(.*?)</pre>", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <summary>
    /// The dump → one result per case. ⛔ Refuses (throws <see cref="InvalidDataException"/>), never skips: no
    /// <c>#out</c>, an unfinished run, a case with no result, a measurement taken before <c>load</c> (mutation M1: a
    /// harness that measured at script run still read the same numbers here, so only this refusal can see it), a
    /// viewport other than asked, a <c>devicePixelRatio</c>
    /// other than 1, a snapshot missing, and a requested control with no rectangle.
    /// </summary>
    public static IReadOnlyDictionary<string, EdgeCaseResult> Parse(string dump, IReadOnlyList<EdgeCase> cases)
    {
        var match = Out.Match(dump);
        if (!match.Success)
        {
            throw new InvalidDataException("the harness page has no #out: Edge dumped something else:\n" + Head(dump));
        }

        if (!match.Groups[1].Value.Contains("data-done=\"1\"", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "the harness did not finish before Edge dumped the page (virtual-time budget, or a case that never reported):\n" +
                Head(WebUtility.HtmlDecode(match.Groups[2].Value)));
        }

        using var json = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups[2].Value));
        var byName = json.RootElement.EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString()!, e => e.Clone());

        var results = new Dictionary<string, EdgeCaseResult>();
        foreach (var c in cases)
        {
            if (!byName.TryGetValue(c.Name, out var r))
            {
                throw new InvalidDataException($"the case '{c.Name}' never reported");
            }

            var errors = r.GetProperty("errors").EnumerateArray().Select(e => e.GetString() ?? "").ToList();
            if (!r.TryGetProperty("vw", out var vwElement))
            {
                throw new InvalidDataException($"'{c.Name}' reported nothing measured: {string.Join("; ", errors)}");
            }

            // ⛔ The measuring script's OWN failures (a step it could not take — a dropdown no rule opens, a missing
            // element) are refused here, never carried: the snapshots after them measured a page the step never changed.
            // The page's errors (App.js) are carried for the test to assert.
            var harnessErrors = errors.Where(e => e.StartsWith("harness:", StringComparison.Ordinal)).ToList();
            if (harnessErrors.Count > 0)
            {
                throw new InvalidDataException($"'{c.Name}': the measuring script failed: {string.Join("; ", harnessErrors)}");
            }

            // ⛔ Measured after load or not at all: before it, an image may still be undecoded — and an <img> that has
            // not loaded stretches under two insets, which hides the very defect the Picture case exists for.
            var readyState = r.GetProperty("rs").GetString();
            if (readyState != "complete")
            {
                throw new InvalidDataException(
                    $"'{c.Name}' was measured with document.readyState '{readyState}', before the page's load event");
            }

            var (vw, vh) = (vwElement.GetInt32(), r.GetProperty("vh").GetInt32());
            if ((vw, vh) != (c.Width, c.Height))
            {
                throw new InvalidDataException(
                    $"'{c.Name}': asked for a {c.Width}x{c.Height} viewport and the page saw {vw}x{vh}: the iframe's size did not take");
            }

            var dpr = r.GetProperty("dpr").GetDouble();
            if (dpr != 1)
            {
                throw new InvalidDataException(
                    $"'{c.Name}' ran at devicePixelRatio {dpr}: --force-device-scale-factor=1 did not take, so its numbers are not CSS px at scale 1");
            }

            var snapshots = new List<LayoutSnapshot>();
            var snaps = r.GetProperty("snaps").EnumerateArray().ToDictionary(s => s.GetProperty("label").GetString()!);
            foreach (var label in new[] { "initial" }.Concat(c.Steps.Select(s => s.Label)))
            {
                if (!snaps.TryGetValue(label, out var snap))
                {
                    throw new InvalidDataException(
                        $"'{c.Name}' took no snapshot '{label}'{(errors.Count > 0 ? ": " + string.Join("; ", errors) : "")}");
                }

                var controls = new Dictionary<string, LayoutBox>();
                var measured = snap.GetProperty("controls");
                foreach (var id in c.Ids)
                {
                    if (!measured.TryGetProperty(id, out var box))
                    {
                        throw new InvalidDataException($"'{c.Name}' '{label}': the page has no element for '{id}'");
                    }

                    controls[id] = Box(box);
                }

                snapshots.Add(new LayoutSnapshot(label, vw, vh, controls));
            }

            var form = r.GetProperty("form");
            var formArea = new LayoutBox(Num(form, "x"), Num(form, "y"), Num(form, "w"), Num(form, "h"));
            var images = r.GetProperty("images").EnumerateObject().ToDictionary(
                p => p.Name,
                p => new EdgeImage(p.Value.GetProperty("c").GetBoolean(), Num(p.Value, "nw"), Num(p.Value, "nh")));
            var literal = r.GetProperty("literal");

            results[c.Name] = new EdgeCaseResult(
                c, vw, vh, dpr, r.GetProperty("ua").GetString() ?? "",
                formArea, (formArea.X + Num(form, "cl"), formArea.Y + Num(form, "ct")),
                r.GetProperty("sw").GetDouble(), r.GetProperty("sh").GetDouble(),
                snapshots,
                r.GetProperty("probes").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? ""),
                errors, images,
                literal.ValueKind == JsonValueKind.Null ? null : Box(literal));
        }

        return results;
    }

    private static LayoutBox Box(JsonElement box) =>
        new(Num(box, "x"), Num(box, "y"), Num(box, "w"), Num(box, "h"), box.GetProperty("v").GetBoolean());

    private static double Num(JsonElement element, string name) => element.GetProperty(name).GetDouble();

    private static string Head(string text) => text.Length <= 2000 ? text : text[..2000] + "…";

    // ================================================================== the pages

    private static string HarnessPage(IReadOnlyList<EdgeCase> cases)
    {
        var data = JsonSerializer.Serialize(cases.Select(c => new
        {
            name = c.Name, form = c.Form, width = c.Width, height = c.Height, ids = c.Ids,
            steps = c.Steps.Select(s => new { label = s.Label, kind = s.Kind, id = s.Id, value = s.Value, x = s.X, y = s.Y })
        }));

        // ⚠ JSON inside a <script>: "</" must not close it.
        return HarnessTemplate
            .Replace("/*CASES*/", data.Replace("</", "<\\/", StringComparison.Ordinal))
            .Replace("/*TIMEOUT*/0", PerCaseTimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private const string HarnessTemplate = """
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><title>vgs layout harness</title>
        <style>body{margin:0} iframe{position:absolute;left:0;top:0;border:0;margin:0;padding:0}</style>
        </head><body>
        <pre id="out"></pre>
        <script>
        (function () {
          var cases = /*CASES*/;
          var results = [], index = 0, timer = null, current = null;
          window.vgsCase = function (name) { return cases.filter(function (c) { return c.name === name; })[0]; };
          window.vgsReport = function (result) {
            if (!current || result.name !== current.name) return;
            clearTimeout(timer);
            results.push(result);
            next();
          };
          function next() {
            var old = document.querySelector("iframe");
            if (old) old.parentNode.removeChild(old);
            if (index >= cases.length) {
              var out = document.getElementById("out");
              out.textContent = JSON.stringify(results);
              out.setAttribute("data-done", "1");
              return;
            }
            current = cases[index++];
            var frame = document.createElement("iframe");
            // ⛔ The case's viewport: the parser refuses a page that saw any other size.
            frame.style.width = current.width + "px";
            frame.style.height = current.height + "px";
            frame.src = current.form + ".measure.html?case=" + encodeURIComponent(current.name);
            var name = current.name;
            timer = setTimeout(function () {
              results.push({ name: name, errors: ["harness: the case never reported"], snaps: [] });
              current = null;
              next();
            }, /*TIMEOUT*/0);
            document.body.appendChild(frame);
          }
          next();
        })();
        </script>
        </body></html>
        """;

    /// <summary>The measuring script, appended (classic) to a served copy of each page.</summary>
    internal const string MeasureScript = """
        // vgs-measure: Task 13's measuring script. Reports every requested control's border box in FORM-CLIENT px.
        (function () {
          "use strict";
          var name = new URLSearchParams(location.search).get("case");
          var spec = parent.vgsCase(name);
          var errors = [], probes = {};
          window.addEventListener("error", function (e) { errors.push(String(e.message || e)); });
          window.addEventListener("unhandledrejection", function (e) { errors.push("unhandled rejection: " + String(e.reason)); });

          // M-5: the reflow script asks getComputedStyle once per docked node per walk; count the calls.
          var nativeStyle = window.getComputedStyle.bind(window), styleCalls = 0;
          window.getComputedStyle = function (el, pseudo) { styleCalls++; return nativeStyle(el, pseudo); };

          function turn() { return new Promise(function (resolve) { setTimeout(resolve, 0); }); }
          function formArea() { return document.querySelector(".vgs-form"); }
          function origin() {
            var f = formArea(), r = f.getBoundingClientRect();
            return { x: r.left + f.clientLeft, y: r.top + f.clientTop };
          }
          function box(el, o) {
            var r = el.getBoundingClientRect();
            return { x: r.left - o.x, y: r.top - o.y, w: r.width, h: r.height, v: el.getClientRects().length > 0 };
          }
          function snap(label) {
            var o = origin(), controls = {};
            spec.ids.forEach(function (id) {
              var el = document.getElementById(id);
              if (el) controls[id] = box(el, o);
            });
            return { label: label, controls: controls };
          }
          function element(id) {
            var el = document.getElementById(id);
            if (!el) throw new Error("no element '" + id + "'");
            return el;
          }
          async function apply(step) {
            switch (step.kind) {
              case "display": element(step.id).style.display = step.value; break;
              case "hidden": element(step.id).hidden = step.value === "true"; break;
              case "class":
                if (!document.getElementById("vgs-harness-style")) {
                  var style = document.createElement("style");
                  style.id = "vgs-harness-style";
                  style.textContent = ".vgs-harness-hide{display:none !important}";
                  document.head.appendChild(style);
                }
                if (step.value === "add") element(step.id).classList.add("vgs-harness-hide");
                else element(step.id).classList.remove("vgs-harness-hide");
                break;
              case "open":
                // B4: a :hover cannot be sent under --dump-dom, so the page's OWN hover rule is found and its display
                // applied inline — never a hard-coded value (review I-2: a catalog rule that no longer opens on hover
                // must turn this red). No rule whose owner matches the item → an error, which the parser refuses.
                var item = element(step.id), list = item.querySelector(":scope > ul"), opened = null;
                if (!list) throw new Error("'" + step.id + "' has no dropdown list");
                var hoverList = /:hover\s*>\s*ul$/;
                Array.prototype.forEach.call(document.styleSheets, function (sheet) {
                  var rules;
                  try { rules = sheet.cssRules; } catch (e) { return; }
                  Array.prototype.forEach.call(rules, function (rule) {
                    if (!rule.selectorText || !rule.style.display) return;
                    rule.selectorText.split(",").forEach(function (part) {
                      var selector = part.trim();
                      if (hoverList.test(selector) && item.matches(selector.replace(hoverList, ""))) opened = rule.style.display;
                    });
                  });
                });
                if (!opened) throw new Error("no stylesheet rule opens '" + step.id + "' on :hover (no ':hover > ul' rule matches it)");
                list.style.display = opened;
                break;
              case "hit":
                var o = origin(), hit = document.elementFromPoint(o.x + step.x, o.y + step.y);
                while (hit && !hit.id) hit = hit.parentElement;
                probes[step.label] = hit ? hit.id : "(none)";
                break;
              case "text":
                probes[step.label] = String(formArea().innerText.indexOf(step.value) >= 0);
                break;
              case "style":
                probes[step.label] = nativeStyle(step.id ? element(step.id) : document.body).getPropertyValue(step.value);
                break;
              case "inview":
                var v = element(step.id).getBoundingClientRect();
                var inside = v.width > 0 && v.height > 0 && v.left >= 0 && v.top >= 0 &&
                             v.right <= window.innerWidth && v.bottom <= window.innerHeight;
                probes[step.label] = inside ? "true"
                  : "false " + JSON.stringify({ x: v.left, y: v.top, w: v.width, h: v.height, vw: window.innerWidth, vh: window.innerHeight });
                break;
              case "rect":
                var rr = element(step.id).getBoundingClientRect();
                probes[step.label] = JSON.stringify({ x: rr.left, y: rr.top, w: rr.width, h: rr.height });
                break;
              case "cost":
                var writes = parseInt(step.value, 10), before = styleCalls, el = element(step.id);
                for (var i = 0; i < writes; i++) {
                  el.style.color = i % 2 === 0 ? "rgb(1, 2, 3)" : "rgb(4, 5, 6)";
                  await turn();
                }
                probes[step.label] = String((styleCalls - before) / writes);
                break;
              default: throw new Error("unknown step " + step.kind);
            }
          }
          async function run() {
            var result = { name: name, errors: errors, probes: probes, snaps: [] };
            try {
              await turn(); await turn();
              var f = formArea(), r = f.getBoundingClientRect();
              result.rs = document.readyState;
              result.vw = window.innerWidth; result.vh = window.innerHeight;
              result.dpr = window.devicePixelRatio; result.ua = navigator.userAgent;
              result.form = { x: r.left, y: r.top, w: r.width, h: r.height, cl: f.clientLeft, ct: f.clientTop };
              result.sw = document.documentElement.scrollWidth; result.sh = document.documentElement.scrollHeight;
              result.images = {};
              Array.prototype.forEach.call(f.querySelectorAll("img[id]"), function (img) {
                result.images[img.id] = { c: img.complete, nw: img.naturalWidth, nh: img.naturalHeight };
              });
              var literal = document.querySelector(".vgs-literal");
              result.literal = literal ? box(literal, origin()) : null;
              result.snaps.push(snap("initial"));
              for (var s = 0; s < spec.steps.length; s++) {
                await apply(spec.steps[s]);
                await turn();
                result.snaps.push(snap(spec.steps[s].label));
              }
            } catch (e) {
              errors.push("harness: " + (e && e.stack ? e.stack : e));
            }
            parent.vgsReport(result);
          }
          // ⛔ On load: images decoded, stylesheet applied. Never at script run.
          window.addEventListener("load", function () { run(); });
        })();
        """;

    // ================================================================== a real image

    /// <summary>
    /// A 1×1 opaque PNG — a REAL image with an intrinsic size nothing like any box it is put in, so an <c>&lt;img&gt;</c>
    /// that keeps its intrinsic size (Task 10 review I-1) cannot hide.
    /// </summary>
    public static byte[] TinyPng()
    {
        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        Chunk("IHDR", new byte[] { 0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0 }); // 1x1, 8-bit RGB

        using (var raw = new MemoryStream())
        {
            using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(new byte[] { 0, 0x33, 0x66, 0x99 }); // filter 0, one pixel
            }

            Chunk("IDAT", raw.ToArray());
        }

        Chunk("IEND", Array.Empty<byte>());
        return png.ToArray();

        void Chunk(string type, byte[] data)
        {
            var typeBytes = Encoding.ASCII.GetBytes(type);
            png.Write(BigEndian((uint)data.Length));
            png.Write(typeBytes);
            png.Write(data);
            png.Write(BigEndian(Crc32(typeBytes.Concat(data).ToArray())));
        }
    }

    private static byte[] BigEndian(uint value) =>
        new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
