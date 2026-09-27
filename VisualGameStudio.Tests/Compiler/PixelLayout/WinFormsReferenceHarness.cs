using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler.PixelLayout;

/// <summary>
/// One control's rectangle in FORM-CLIENT pixels (spec 2026-09-27 §7 item 2) — the unit both reference harnesses
/// report in: WinForms in 96-DPI logical pixels, Edge in CSS pixels at <c>--force-device-scale-factor=1</c>.
/// ⚠ A hidden control (<see cref="Visible"/> false) is compared by visibility only; its X/Y are whatever the side
/// that measured it could say (WinForms: its parent-relative Location).
/// </summary>
internal readonly record struct LayoutBox(double X, double Y, double Width, double Height, bool Visible = true);

/// <summary>Every control of one form at one moment: after the form is shown (<c>design</c>), or after a step.</summary>
internal sealed record LayoutSnapshot(
    string Label, int ClientWidth, int ClientHeight, IReadOnlyDictionary<string, LayoutBox> Controls);

/// <summary>
/// What the real WinForms window did with one form — the reference Task 13's Edge page is compared against.
/// </summary>
/// <param name="DeviceDpi">The form's DeviceDpi. Always 96: the parser refuses anything else (the DPI pin).</param>
/// <param name="DisplayScale">The machine's real display scale (physical ÷ logical screen width), recorded only.</param>
internal sealed record WinFormsReference(
    string FormName, int DeviceDpi, double DisplayScale, IReadOnlyList<LayoutSnapshot> Snapshots)
{
    public LayoutSnapshot this[string label] =>
        Snapshots.FirstOrDefault(s => s.Label == label)
        ?? throw new KeyNotFoundException($"{FormName} has no snapshot '{label}' (has: {string.Join(", ", Snapshots.Select(s => s.Label))})");

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>A step the driver takes after showing the form; a snapshot is taken after each.</summary>
internal abstract record ReferenceStep(string Label);

/// <summary><c>form.ClientSize = new Size(Width, Height)</c>.</summary>
internal sealed record ResizeStep(string Label, int Width, int Height) : ReferenceStep(Label);

/// <summary>The control's run-time <c>Visible</c> toggled — its docked siblings re-dock.</summary>
internal sealed record VisibilityStep(string Label, string Id, bool Visible) : ReferenceStep(Label);

/// <summary>A Canvas page to measure, and what to do to it.</summary>
/// <param name="PinStrips">Pin every strip <c>AutoSize = false</c> at its catalog <c>DefaultHeight</c> (spec §7
/// item 5). False measures the strips' real content height, to record the difference.</param>
internal sealed record ReferenceFixture(FormDocument Document, IReadOnlyList<ReferenceStep> Steps, bool PinStrips = true)
{
    public ReferenceFixture(FormDocument document, params ReferenceStep[] steps) : this(document, steps, true) { }

    public string FormName => Document.Name;

    /// <summary>
    /// Every control the driver measures: each positioned control and each strip, at every depth. Not items (a
    /// <c>ToolStripMenuItem</c> is not a Control) and not tray components (no place at all).
    /// </summary>
    public IReadOnlyList<string> Ids =>
        Document.AllControls().Where(c => c.Definition?.Place != FormPlace.Item).Select(c => c.Id).ToList();

    public IReadOnlyList<(string Id, int Height)> Pins => PinStrips
        ? Document.AllControls().Where(c => c.Definition?.Place == FormPlace.Docked)
            .Select(c => (c.Id, c.Definition!.DefaultHeight)).ToList()
        : Array.Empty<(string, int)>();

    public DriverPlan Plan()
    {
        var size = Document.DesignSize;
        var snapshots = new List<(string, int, int)> { ("design", size.Width, size.Height) };
        foreach (var step in Steps)
        {
            if (step is ResizeStep resize)
            {
                size = (resize.Width, resize.Height);
            }

            snapshots.Add((step.Label, size.Width, size.Height));
        }

        return new DriverPlan(FormName, Ids, snapshots);
    }
}

/// <summary>What the parser holds the driver's output to: every snapshot at its requested size, every id in each.</summary>
internal sealed record DriverPlan(
    string FormName, IReadOnlyList<string> Ids, IReadOnlyList<(string Label, int Width, int Height)> Snapshots);

/// <summary>
/// ⛔⛔ Task 12 — the real WinForms window as the reference for resize behaviour (spec 2026-09-27 §7 items 1–6).
/// Retargets each Canvas page to WinForms (<see cref="FormRetarget.ConvertToPair"/>), compiles each pair with the
/// real CLI, builds ONE WinForms app whose driver measures every form in turn, runs it once and parses its output.
///
/// <para>The driver, and its rules (each one found necessary, not decorative):</para>
/// <list type="bullet">
///   <item><description>⛔ <c>Application.SetHighDpiMode(HighDpiMode.DpiUnaware)</c> before any window: every
///   number is a 96-DPI logical pixel, the unit Edge's CSS pixels are in at scale factor 1. The driver prints
///   <c>DeviceDpi</c> and <see cref="Parse"/> REFUSES anything but 96, so a lost pin fails rather than reporting
///   scaled numbers. The machine's real scale is recorded (<see cref="WinFormsReference.DisplayScale"/>).</description></item>
///   <item><description>⛔ Controls are found through the form's private FIELDS by <c>Id</c> — the generated code
///   never sets <c>Control.Name</c> (chip task_fa51e644). A missing field THROWS in the driver.</description></item>
///   <item><description>Strips are pinned <c>AutoSize = false</c> at the catalog height between the constructor
///   and <c>Show</c> (spec §7 item 5; anchors do not depend on them, §7a).</description></item>
///   <item><description>⛔ A control's rectangle is its WINDOW rectangle: its <c>Location</c> mapped from its
///   parent's client area to the form's (<c>form.PointToClient(c.Parent.PointToScreen(c.Location))</c>) + its
///   <c>Size</c>. Spec §7 item 2's <c>c.PointToScreen(Point.Empty)</c> is the control's CLIENT origin, which a
///   bordered Panel insets — measured 1px (FixedSingle) and 2px (Fixed3D) inside its own box. Edge's
///   <c>getBoundingClientRect</c> is the border box, i.e. the window rectangle.</description></item>
///   <item><description>The form is SHOWN (a created handle; <c>PointToScreen</c> needs one), placed at the
///   working area's top-left, off the taskbar and fully transparent, so the owner's screen is not disturbed.</description></item>
///   <item><description>After every step: <c>PerformLayout</c>, <c>DoEvents</c>, then the ACTUAL client size is
///   printed and <see cref="Parse"/> refuses a snapshot whose size is not the one asked for (Windows clamps an
///   oversized window).</description></item>
/// </list>
/// <para>⚠ The process is run through <see cref="CliTestHarness.RunProcess"/>, which kills ITS OWN process tree on
/// timeout — never by name.</para>
/// </summary>
internal static class WinFormsReferenceHarness
{
    /// <summary>The only DeviceDpi whose numbers are CSS pixels.</summary>
    public const int ExpectedDpi = 96;

    private static readonly Regex Token = new("^[A-Za-z_][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Measures every fixture in one WinForms build and run. Skips (never passes) off Windows or without dotnet.
    /// Writes <c>&lt;workDir&gt;/&lt;Form&gt;.reference.json</c> for each form.
    /// </summary>
    public static IReadOnlyDictionary<string, WinFormsReference> Measure(string workDir, params ReferenceFixture[] fixtures)
    {
        if (!OperatingSystem.IsWindows())
        {
            TestSkip.IgnoreEvenInsideMultiple("a WinForms window can only be run on Windows");
        }

        if (!CliTestHarness.DotnetOnPath())
        {
            TestSkip.IgnoreEvenInsideMultiple("the dotnet SDK is not on PATH, so the WinForms reference app cannot be built");
        }

        var names = fixtures.Select(f => f.FormName).ToList();
        Assert.That(names, Is.Unique, "every fixture needs its own form name: each is a class in one program");

        var app = Path.Combine(workDir, "reference-app");
        Directory.CreateDirectory(app);

        foreach (var fixture in fixtures)
        {
            var pair = FormRetarget.ConvertToPair(fixture.Document, FormTarget.WinForms);
            var bas = Path.Combine(workDir, pair.CodeFileName);
            File.WriteAllText(bas, pair.CodeText);

            var (exit, stdout, stderr) = CliTestHarness.RunProcess(
                CliTestHarness.CliPath(), new[] { bas, "--target=csharp" }, workDir, timeoutMs: 180_000);
            Assert.That(exit, Is.Zero,
                $"the real CLI refused {fixture.FormName}'s retargeted pair.\n{stdout}\n{stderr}\n--- .bas ---\n{pair.CodeText}");

            var generated = Path.ChangeExtension(bas, ".cs");
            Assert.That(File.Exists(generated), Is.True, $"no C# emitted for {fixture.FormName}.\n{stdout}");
            File.Copy(generated, Path.Combine(app, fixture.FormName + ".cs"), overwrite: true);
        }

        File.WriteAllText(Path.Combine(app, "app.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWindowsForms>true</UseWindowsForms>
                <Nullable>disable</Nullable>
                <AssemblyName>ReferenceDriver</AssemblyName>
                <RootNamespace>ReferenceDriver</RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(app, "Driver.cs"), DriverSource(fixtures));

        var (buildExit, buildOut, buildErr) = CliTestHarness.RunProcess(
            "dotnet", new[] { "build", "-c", "Release", "--nologo" }, app, timeoutMs: 300_000);
        Assert.That(buildExit, Is.Zero, $"the WinForms reference app did not build.\n{buildOut}\n{buildErr}");

        var exe = Directory.GetFiles(app, "ReferenceDriver.exe", SearchOption.AllDirectories).FirstOrDefault();
        Assert.That(exe, Is.Not.Null, "the reference app built but produced no ReferenceDriver.exe");

        var (runExit, runOut, runErr) = CliTestHarness.RunProcess(exe!, Array.Empty<string>(), app, timeoutMs: 120_000);
        TestContext.Out.WriteLine("[reference driver output]\n" + runOut + runErr);
        Assert.That(runExit, Is.Zero, $"the reference driver failed (exit {runExit}).\n{runOut}\n{runErr}");

        var references = Parse(runOut, fixtures.Select(f => f.Plan()).ToList());
        foreach (var reference in references.Values)
        {
            File.WriteAllText(Path.Combine(workDir, reference.FormName + ".reference.json"), reference.ToJson());
        }

        return references;
    }

    /// <summary>
    /// The driver's output → one <see cref="WinFormsReference"/> per plan. ⛔ Refuses (throws
    /// <see cref="InvalidDataException"/>), never skips: an <c>ERROR</c> line, no <c>DONE</c>, a DeviceDpi that is
    /// not 96, a snapshot missing or at a size other than the one asked for, and an id with no rectangle.
    /// </summary>
    public static IReadOnlyDictionary<string, WinFormsReference> Parse(string output, IReadOnlyList<DriverPlan> plans)
    {
        var lines = output.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        var error = lines.FirstOrDefault(l => l.StartsWith("ERROR ", StringComparison.Ordinal));
        if (error != null)
        {
            throw new InvalidDataException("the reference driver failed: " + error);
        }

        if (!lines.Contains("DONE"))
        {
            throw new InvalidDataException("the reference driver never printed DONE; its output was cut short:\n" + output);
        }

        var screen = lines.FirstOrDefault(l => l.StartsWith("SCREEN ", StringComparison.Ordinal))?.Split(' ')
            ?? throw new InvalidDataException("the reference driver printed no SCREEN line");
        var scale = Int(screen[2]) / (double)Int(screen[1]);

        var dpi = new Dictionary<string, int>();
        var sizes = new Dictionary<(string, string), (int, int)>();
        var rects = new Dictionary<(string, string), Dictionary<string, LayoutBox>>();

        foreach (var parts in lines.Select(l => l.Split(' ')))
        {
            switch (parts[0])
            {
                case "FORM":
                    dpi[parts[1]] = Int(parts[2]);
                    break;
                case "SNAP":
                    sizes[(parts[1], parts[2])] = (Int(parts[3]), Int(parts[4]));
                    break;
                case "RECT":
                    if (!rects.TryGetValue((parts[1], parts[2]), out var boxes))
                    {
                        rects[(parts[1], parts[2])] = boxes = new Dictionary<string, LayoutBox>();
                    }

                    boxes[parts[3]] = new LayoutBox(
                        Int(parts[5]), Int(parts[6]), Int(parts[7]), Int(parts[8]), Visible: parts[4] == "1");
                    break;
            }
        }

        var result = new Dictionary<string, WinFormsReference>();
        foreach (var plan in plans)
        {
            if (!dpi.TryGetValue(plan.FormName, out var formDpi))
            {
                throw new InvalidDataException($"the driver never showed {plan.FormName} (no FORM line)");
            }

            if (formDpi != ExpectedDpi)
            {
                throw new InvalidDataException(
                    $"{plan.FormName} reported DeviceDpi {formDpi}: the DpiUnaware pin was lost, so its numbers are " +
                    $"not {ExpectedDpi}-DPI pixels and cannot be compared with CSS pixels");
            }

            var snapshots = new List<LayoutSnapshot>();
            foreach (var (label, width, height) in plan.Snapshots)
            {
                if (!sizes.TryGetValue((plan.FormName, label), out var actual))
                {
                    throw new InvalidDataException($"the driver took no snapshot '{label}' of {plan.FormName}");
                }

                if (actual != (width, height))
                {
                    throw new InvalidDataException(
                        $"{plan.FormName} '{label}': asked for a {width}x{height} client area and got " +
                        $"{actual.Item1}x{actual.Item2} — Windows clamped the window; pick a size inside the working area");
                }

                var boxes = rects.GetValueOrDefault((plan.FormName, label)) ?? new Dictionary<string, LayoutBox>();
                var controls = new Dictionary<string, LayoutBox>();
                foreach (var id in plan.Ids)
                {
                    if (!boxes.TryGetValue(id, out var box))
                    {
                        throw new InvalidDataException(
                            $"the driver reported no rectangle for '{id}' in {plan.FormName} '{label}'");
                    }

                    controls[id] = box;
                }

                snapshots.Add(new LayoutSnapshot(label, width, height, controls));
            }

            result[plan.FormName] = new WinFormsReference(plan.FormName, formDpi, scale, snapshots);
        }

        return result;
    }

    private static int Int(string text) => int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private static string Checked(string token, string what) =>
        Token.IsMatch(token) ? token : throw new ArgumentException($"{what} '{token}' must be a plain token (no spaces)");

    /// <summary>The driver: a fixed runtime part and one block per fixture.</summary>
    internal static string DriverSource(IReadOnlyList<ReferenceFixture> fixtures)
    {
        var blocks = new StringBuilder();
        foreach (var fixture in fixtures)
        {
            var name = Checked(fixture.FormName, "form name");
            var ids = string.Join(", ", fixture.Ids.Select(id => $"\"{Checked(id, "id")}\""));
            var pins = string.Join(", ", fixture.Pins.Select(p => $"(\"{Checked(p.Id, "id")}\", {p.Height})"));

            blocks.Append($"            using (var m = new Measurer(new global::GeneratedCode.{name}(), \"{name}\", new string[] {{ {ids} }}, new (string, int)[] {{ {pins} }}))\n");
            blocks.Append("            {\n");
            foreach (var step in fixture.Steps)
            {
                var label = Checked(step.Label, "label");
                blocks.Append(step switch
                {
                    ResizeStep r => $"                m.Resize(\"{label}\", {r.Width}, {r.Height});\n",
                    VisibilityStep v => $"                m.SetVisible(\"{label}\", \"{Checked(v.Id, "id")}\", {(v.Visible ? "true" : "false")});\n",
                    _ => throw new ArgumentException($"unknown step {step}")
                });
            }

            blocks.Append("            }\n");
        }

        return DriverTemplate.Replace("//FIXTURES", blocks.ToString());
    }

    private const string DriverTemplate = """
        using System;
        using System.Collections.Generic;
        using System.Drawing;
        using System.Globalization;
        using System.Linq;
        using System.Reflection;
        using System.Runtime.InteropServices;
        using System.Windows.Forms;

        internal static class Driver
        {
            [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
            [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
            [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr dc, int index);

            [STAThread]
            private static int Main()
            {
                // ⛔ Before any window: every number below is a 96-DPI logical pixel (spec §7 item 4).
                Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

                try
                {
                    // HORZRES (8) is the logical width an unaware process sees; DESKTOPHORZRES (118) the physical one.
                    var dc = GetDC(IntPtr.Zero);
                    var logical = GetDeviceCaps(dc, 8);
                    var physical = GetDeviceCaps(dc, 118);
                    ReleaseDC(IntPtr.Zero, dc);
                    var area = Screen.PrimaryScreen.WorkingArea;
                    Console.WriteLine($"SCREEN {logical} {physical} {area.X} {area.Y} {area.Width} {area.Height}");

        //FIXTURES
                    Console.WriteLine("DONE");
                    return 0;
                }
                catch (Exception e)
                {
                    Console.WriteLine("ERROR " + e.ToString().Replace("\r", " ").Replace("\n", " "));
                    return 3;
                }
            }
        }

        internal sealed class Measurer : IDisposable
        {
            private readonly Form _form;
            private readonly string _name;
            private readonly List<KeyValuePair<string, Control>> _controls = new List<KeyValuePair<string, Control>>();

            public Measurer(Form form, string name, string[] ids, (string Id, int Height)[] pins)
            {
                _form = form;
                _name = name;

                // ⛔ By FIELD: the generated code never sets Control.Name. A missing field is an error, never a skip.
                foreach (var id in ids)
                {
                    var field = form.GetType().GetField(id, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (field == null)
                    {
                        throw new MissingFieldException(form.GetType().FullName, id);
                    }

                    var control = field.GetValue(form) as Control;
                    if (control == null)
                    {
                        throw new InvalidOperationException("field '" + id + "' of " + name + " holds no Control");
                    }

                    _controls.Add(new KeyValuePair<string, Control>(id, control));
                }

                // Strips at their catalog height, before the first layout (spec §7 item 5, §7a).
                foreach (var pin in pins)
                {
                    var strip = (ToolStrip)Find(pin.Id);
                    strip.AutoSize = false;
                    strip.Height = pin.Height;
                }

                _form.StartPosition = FormStartPosition.Manual;
                _form.Location = Screen.PrimaryScreen.WorkingArea.Location;
                _form.ShowInTaskbar = false;
                _form.Opacity = 0;
                _form.Show();
                Settle();

                Console.WriteLine("FORM " + _name + " " + _form.DeviceDpi);
                Snapshot("design");
            }

            public void Resize(string label, int width, int height)
            {
                _form.ClientSize = new Size(width, height);
                Settle();
                Snapshot(label);
            }

            public void SetVisible(string label, string id, bool visible)
            {
                Find(id).Visible = visible;
                Settle();
                Snapshot(label);
            }

            private Control Find(string id) => _controls.First(c => c.Key == id).Value;

            private void Settle()
            {
                _form.PerformLayout();
                Application.DoEvents();
            }

            private void Snapshot(string label)
            {
                Console.WriteLine("SNAP " + _name + " " + label + " " + _form.ClientSize.Width + " " + _form.ClientSize.Height);
                foreach (var pair in _controls)
                {
                    var c = pair.Value;
                    if (c.Visible)
                    {
                        // Form-client coordinates (spec §7 item 2), through the screen so every ANCESTOR's
                        // client-area inset (a border) is in the number. ⛔ The control's own Location mapped
                        // from its PARENT's client area — never c.PointToScreen(Point.Empty), which is the
                        // control's own CLIENT origin: a bordered Panel then reads 1-2px inside its own box
                        // (measured: FixedSingle (201,11) for a Panel at (200,10)).
                        var p = _form.PointToClient(c.Parent.PointToScreen(c.Location));
                        Console.WriteLine("RECT " + _name + " " + label + " " + pair.Key + " 1 " + p.X + " " + p.Y + " " + c.Width + " " + c.Height);
                    }
                    else
                    {
                        // Hidden: its parent-relative Location; compared by visibility only.
                        Console.WriteLine("RECT " + _name + " " + label + " " + pair.Key + " 0 " + c.Left + " " + c.Top + " " + c.Width + " " + c.Height);
                    }
                }
            }

            public void Dispose()
            {
                _form.Close();
                _form.Dispose();
            }
        }
        """;
}
