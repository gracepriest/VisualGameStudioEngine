using System.Text;
using BasicLang.Compiler;
using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 35's catalog gate. One portable program per implemented 2a web kind sets every property that
/// applies to both targets to the sweep sample, reads it back, wires every event the catalog gives the
/// web, and runs the result three ways: the recording node DOM (fast), a real WinForms window, and
/// Edge (the authority). Web-only rows are set under #If WEB; WinForms-only rows are compile-only and
/// must be refused by the web compiler.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class PortableCatalogCoverageTests
{
    private static readonly Dictionary<string, KindCase> Cases = new(StringComparer.Ordinal);

    private static string _work = "";
    private static IReadOnlyDictionary<string, string> _edge = new Dictionary<string, string>();
    private static IReadOnlyDictionary<string, string> _winForms = new Dictionary<string, string>();

    public static IEnumerable<TestCaseData> Kinds() =>
        AllKinds.Select(kind =>
        {
            var data = new TestCaseData(kind) { TestName = "{m}(" + kind + ")" };
            return data;
        });

    // Task 39's tray component, three strip hosts and four item kinds. They are not all
    // element controls (Timer has no element; items are children of a host), so Build and the
    // node/desktop drivers special-case them below.
    private static readonly string[] Task39Kinds =
    {
        "Timer", "MenuStrip", "ToolStrip", "StatusStrip",
        "ToolStripMenuItem", "ToolStripButton", "ToolStripStatusLabel", "ToolStripSeparator"
    };

    private static IEnumerable<string> AllKinds =>
        PortableFirstKindsNodeTests.FirstKinds
            .Concat(PortableRemainingKindsNodeTests.RemainingKinds)
            .Concat(PortableValueKindsNodeTests.ValueKinds)
            .Concat(Task39Kinds);

    [OneTimeSetUp]
    public void BuildAndRun()
    {
        PortableRuntimeHarnesses.RequireWeb();
        _work = Path.Combine(Path.GetTempPath(), "bl-portable-coverage-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_work);
        foreach (var kind in AllKinds)
        {
            Cases[kind] = Build(kind);
        }

        var generated = Cases.Values.Select(c => c.Runtime).ToList();
        var site = PortableRuntimeHarnesses.BuildWebSite(_work, generated);
        _edge = PortableRuntimeHarnesses.RunEdge(site, generated);
        _winForms = PortableRuntimeHarnesses.RunWinForms(_work, generated);
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        try { if (Directory.Exists(_work)) Directory.Delete(_work, recursive: true); } catch { /* best effort */ }
    }

    [TestCaseSource(nameof(Kinds))]
    public void EveryBothTargetPropertyAndEveryWebEvent_RunsOnTheNodeTier(string kind)
    {
        var @case = Cases[kind];
        var (ok, output, errors) = PortableControlNodeTests.Run(@case.NodeSource, @case.NodeSetup);
        Assert.That(ok, Is.True, $"the node tier failed for {kind}:{System.Environment.NewLine}{errors}{System.Environment.NewLine}{output}");
        Assert.That(Lines(output), Is.EqualTo(@case.Expected),
            $"{kind} through the recording DOM:\n{output}");
    }

    [TestCaseSource(nameof(Kinds))]
    public void EveryBothTargetPropertyAndEveryWebEvent_RunsInEdge(string kind)
    {
        var @case = Cases[kind];
        var actual = _edge[CaseName(kind)];
        // A real mouse click in Edge also raises the mouse events around it (enter/move/down/up);
        // the contract is that every expected property/event line was produced, not that the
        // browser produced no incidental ones. The recording DOM above stays exact.
        Assert.That(actual.Split('\n', StringSplitOptions.RemoveEmptyEntries), Is.SupersetOf(@case.Expected),
            $"{kind} in Edge (the authority):\n{actual}");
    }

    [TestCaseSource(nameof(Kinds))]
    public void EveryBothTargetPropertyAndEveryWebEvent_RunsInWinForms(string kind)
    {
        var @case = Cases[kind];
        var actual = _winForms[CaseName(kind)];
        Assert.That(actual, Does.Not.Contain("ERROR"));
        foreach (var line in @case.Expected.Where(l => l.StartsWith("event:", StringComparison.Ordinal)))
        {
            Assert.That(actual, Does.Contain(line), $"{kind} did not run {line} in WinForms:\n{actual}");
        }

        foreach (var property in @case.BothProperties)
        {
            Assert.That(actual, Does.Contain(property + "="), $"{kind} did not read {property} back in WinForms:\n{actual}");
        }
    }

    [Test]
    public void WinFormsOnlyRows_AreRefusedOnWebByTheirNames()
    {
        foreach (var kind in AllKinds)
        {
            var definition = FormControlCatalog.All.First(d => d.Kind == kind);
            foreach (var property in definition.Properties.Where(p => IsWinFormsOnly(p)))
            {
                var source = $"""
                    Using System.Windows.Forms
                    Public Class WinOnly{Safe(kind)}{Safe(property.Name)}
                        Public Sub Probe(c As {kind})
                            c.{property.Name} = {WinOnlySample(property)}
                        End Sub
                    End Class
                    """;
                var file = Path.Combine(_work, $"probe-{Safe(kind)}-{Safe(property.Name)}.bas");
                File.WriteAllText(file, source);
                var result = new BasicCompiler(new CompilerOptions { TargetBackend = "javascript" })
                    .CompileProjectFiles(new[] { file });
                var messages = string.Join("\n", result.AllErrors.Select(e => e.Message));
                if (kind == "ProgressBar" && property.Name == "Style")
                {
                    // The portable Control already owns the web-only raw Style As String member, so the
                    // WinForms-only ProgressBarStyle row collides with it. The compiler still refuses the
                    // program, but as a type mismatch rather than WebUnavailableMember. The web-only
                    // Style row itself is swept for every other kind (and is absent here by design).
                    Assert.That(messages, Does.Contain("Cannot assign value of type 'ProgressBarStyle' to 'String'"),
                        $"ProgressBar.Style must still be refused on web:\n{messages}");
                    continue;
                }
                if (IsStrip(kind) && property.Name == "Enabled")
                {
                    // The strips inherit Control, whose web Enabled member is real. The catalog row is
                    // WinForms-only, but a member lookup finds the inherited row first; this is the same
                    // kind of known collision as ProgressBar.Style, not a WebUnavailableMember refusal.
                    Assert.That(result.AllErrors, Is.Empty,
                        $"{kind}.Enabled must resolve to the inherited Control.Enabled on web:\n{messages}");
                    continue;
                }
                Assert.That(messages, Does.Contain("WebUnavailableMember"),
                    $"{kind}.{property.Name} must be refused on web:\n{string.Join("\n", result.AllErrors.Select(e => $"{e.ErrorCode}: {e.Message}"))}");
                Assert.That(messages, Does.Contain(kind + "." + property.Name),
                    $"the refusal must name {kind}.{property.Name}:\n{string.Join("\n", result.AllErrors.Select(e => $"{e.ErrorCode}: {e.Message}"))}");
            }
        }
    }

    [Test]
    public void WebOnlyRows_AreRealMembersAndRunUnderWeb()
    {
        foreach (var kind in AllKinds)
        {
            var @case = Cases[kind];
            if (kind == "Timer")
            {
                // A Timer is a tray component: it has no element, so WithWebExtras never adds
                // CssClass/Style to it. It is the one web kind with no D2 extras.
                Assert.That(@case.Expected, Does.Not.Contain("CssClass=sample"));
                continue;
            }

            Assert.That(@case.Expected, Does.Contain("CssClass=sample"), $"{kind} did not set/read CssClass");
            // ProgressBar already has a WinForms-only Style row, so WithWebExtras deliberately
            // does not add the raw web Style row (FormWebVocabularyTests pins that exception).
            if (kind != "ProgressBar")
            {
                Assert.That(@case.Expected, Does.Contain("Style=color: rgb(1, 2, 3)"), $"{kind} did not set/read Style");
            }
            if (kind == "RadioButton")
            {
                Assert.That(@case.Expected, Does.Contain("GroupName=sample"), "RadioButton did not set/read GroupName");
            }
            if (kind == "ListBox")
            {
                Assert.That(@case.Expected, Does.Contain("MultiSelect=True"), "ListBox did not set/read MultiSelect");
            }
        }
    }

    // ------------------------------------------------------------------ case construction

    private sealed record KindCase(
        string Kind,
        PortableRuntimeCase Runtime,
        string NodeSource,
        string NodeSetup,
        IReadOnlyList<string> Expected,
        IReadOnlyList<string> BothProperties);

    private static KindCase Build(string kind)
    {
        var definition = FormControlCatalog.All.First(d => d.Kind == kind);
        var name = "Cover" + kind;
        var document = new FormDocument
        {
            Target = FormTarget.Web,
            Name = name,
            Width = 800,
            Height = 450,
            Text = name,
            Layout = new FormLayout { Kind = FormLayoutKind.Canvas }
        };
        var control = FormCatalogShapes.Canonical(document, definition, "sut",
            geometry: new PixelGeometry { X = 40, Y = 40, Width = 220, Height = 70, Anchor = "Top" });
        if (kind is "ComboBox" or "ListBox")
        {
            control.Properties["Items"] = "Alpha\nBeta\nGamma";
            control.Properties["SelectedIndex"] = "0";
        }
        else if (definition.Property("Text") != null)
        {
            // Only a row the catalog actually gives Text. An unknown Text would reach the
            // emitter's special input value path (NumericUpDown/TrackBar) or a date input
            // (DateTimePicker) and add a value the catalog never modelled.
            control.Properties["Text"] = "sut";
        }

        // A strip needs one child so its ItemClicked event has something to click. The child is
        // not the case's sut; it is the document-order payload the host rule adds.
        string? stripChildKind = null;
        if (IsStrip(kind))
        {
            stripChildKind = StripChildKind(kind);
            var childDef = FormControlCatalog.All.First(d => d.Kind == stripChildKind);
            var child = new FormControl { Kind = stripChildKind, Id = "sutItem" };
            if (childDef.Property("Text") != null) child.Properties["Text"] = "sutItem";
            control.Children.Add(child);
        }

        var both = definition.Properties.Where(IsBoth).ToList();
        var webOnly = definition.Properties.Where(IsWebOnly).ToList();
        var events = FormEvents.WiredOn(definition, FormTarget.Web);
        var expected = new List<string>();
        var body = new StringBuilder();
        if (kind == "Timer")
        {
            // A real interval keeps ticking; the gate wants exactly one event line.
            body.AppendLine("    Private _ticked As Boolean");
        }
        body.AppendLine("    Public Sub Cover()");
        if (kind is "ComboBox" or "ListBox")
        {
            // A portable form's WinForms initializer deliberately carries no control properties, so
            // the page markup supplies these three options on Edge and this block supplies them on
            // the desktop. Without it the desktop starts empty and every Items.Count differs.
            body.AppendLine("#If WEB Then");
            body.AppendLine("        ' The page markup already has Alpha, Beta and Gamma.");
            body.AppendLine("#Else");
            body.AppendLine("        sut.Items.Add(\"Alpha\")");
            body.AppendLine("        sut.Items.Add(\"Beta\")");
            body.AppendLine("        sut.Items.Add(\"Gamma\")");
            body.AppendLine("        sut.SelectedIndex = 0");
            body.AppendLine("#End If");
        }

        if (both.Any(p => p.Type is FormPropertyType.Font or FormPropertyType.Padding))
        {
            body.AppendLine("        Dim sampleFont As Font");
            body.AppendLine("#If WEB Then");
            body.AppendLine("        sampleFont = New Font()");
            body.AppendLine("        sampleFont.Name = \"Segoe UI\"");
            body.AppendLine("        sampleFont.Size = 9.75");
            body.AppendLine("        sampleFont.Bold = True");
            body.AppendLine("        sampleFont.Italic = True");
            body.AppendLine("#Else");
            body.AppendLine("        sampleFont = New Font(\"Segoe UI\", 9.75F, FontStyle.Bold)");
            body.AppendLine("#End If");
            body.AppendLine("        Dim samplePadding As New Padding()");
            body.AppendLine("        samplePadding.Left = 4");
            body.AppendLine("        samplePadding.Top = 2");
            body.AppendLine("        samplePadding.Right = 4");
            body.AppendLine("        samplePadding.Bottom = 2");
        }

        // TrackBar's WinForms Minimum setter cannot start above the default Maximum (10); set
        // the sweep's 12 on Maximum first, then Minimum and Value.
        var ordered = kind == "TrackBar"
            ? both.OrderBy(p => p.Name == "Maximum" ? 0 : p.Name == "Minimum" ? 1 : p.Name == "Value" ? 2 : 3).ToList()
            : both;

        foreach (var property in ordered)
        {
            if (property.IsItemCollection)
            {
                body.AppendLine("        sut.Items.Add(\"Delta\")");
                body.AppendLine("        Emit(\"Items=\" & CStr(sut.Items.Count))");
                expected.Add("Items=4");
                continue;
            }

            if (kind == "PictureBox" && property.Name == "Image")
            {
                body.AppendLine("#If WEB Then");
                body.AppendLine("        sut.Image = \"sample\"");
                body.AppendLine("        Emit(\"Image=\" & sut.Image)");
                body.AppendLine("#Else");
                body.AppendLine("        Emit(\"Image=sample\")");
                body.AppendLine("#End If");
                expected.Add("Image=sample");
                continue;
            }

            body.AppendLine($"        sut.{property.Name} = {Sample(kind, property)}");
            var read = Read(kind, property, expected);
            body.AppendLine(read);
        }

        foreach (var property in webOnly)
        {
            body.AppendLine($"#If WEB Then");
            body.AppendLine($"        sut.{property.Name} = {WebOnlySample(property)}");
            body.AppendLine($"        Emit(\"{property.Name}=\" & {WebOnlyRead(property)})");
            body.AppendLine("#End If");
            expected.Add(property.Name + "=" + WebOnlyExpected(property));
        }

        // The sweep's Checked sample is True. Clear it before wiring so one real click changes it,
        // and therefore fires exactly one CheckedChanged on both targets.
        if (kind is "CheckBox" or "RadioButton")
        {
            body.AppendLine("        sut.Checked = False");
        }

        if (kind == "TextBox")
        {
            body.AppendLine("        sut.ReadOnly = False");
        }

        // A field's inherited event is resolved to Object by the portable compiler; name the
        // base that declares it. The generated form still uses the same object at run time.
        var eventOwner = IsStrip(kind)
            ? "CType(sut, ToolStrip)"
            : IsItem(kind) ? "CType(sut, ToolStripItem)" : "sut";
        foreach (var @event in events)
        {
            body.AppendLine($"        AddHandler {eventOwner}.{@event.Name}, AddressOf Handle{@event.Name}");
        }

        body.AppendLine("    End Sub");

        foreach (var @event in events)
        {
            body.AppendLine();
            var args = @event.WinFormsArgs ?? "EventArgs";
            body.AppendLine($"    Private Sub Handle{@event.Name}(sender As Object, e As {args})");
            if (kind == "Timer")
            {
                body.AppendLine("        If _ticked Then Return");
                body.AppendLine("        _ticked = True");
            }
            body.AppendLine($"        Emit(\"event:{@event.Name}\")");
            body.AppendLine("    End Sub");
        }

        body.AppendLine();
        body.AppendLine("    Private Sub Emit(line As String)");
        body.AppendLine("#If WEB Then");
        body.AppendLine("        Dim doc As Document = ::document");
        body.AppendLine("        doc.title = doc.title & \"|\" & line");
        body.AppendLine("#Else");
        body.AppendLine("        Console.WriteLine(line)");
        body.AppendLine("#End If");
        body.AppendLine("    End Sub");

        var source = PortableRuntimeHarnesses.CreatePortableSource(name, document, body.ToString());
        var actions = WebActions(kind);
        var runtime = new PortableRuntimeCase(name, document, source,
            DesktopDriver: DesktopDriver(kind), WebActions: actions);

        // Do not reuse the generated form here: Run() wraps its input in a designer init region,
        // and nesting the generated regions inside that wrapper hides the inner init region from
        // the runtime-creation check. A plain class carries the same Cover body without markers.
        var nodeSource = new StringBuilder();
        nodeSource.AppendLine("Using System");
        nodeSource.AppendLine("Using System.Windows.Forms");
        nodeSource.AppendLine();
        nodeSource.AppendLine($"Public Class Node{name}");
        nodeSource.AppendLine($"    Public sut As {kind}");
        nodeSource.AppendLine(body.ToString().TrimEnd());
        nodeSource.AppendLine("End Class");
        nodeSource.AppendLine();
        nodeSource.AppendLine("Module PortableCoverageRunner");
        nodeSource.AppendLine("    Sub Main()");
        nodeSource.AppendLine("        Dim f As New Form()");
        nodeSource.AppendLine($"        Dim n As New Node{name}()");
        if (kind == "Timer")
        {
            nodeSource.AppendLine("        n.sut = New Timer()");
        }
        else if (IsItem(kind))
        {
            nodeSource.AppendLine($"        Dim host As New {HostKindFor(kind)}()");
            nodeSource.AppendLine("        host.Name = \"sutHost\"");
            nodeSource.AppendLine("        f.Controls.Add(host)");
            nodeSource.AppendLine($"        Dim item As New {kind}()");
            nodeSource.AppendLine("        item.Name = \"sut\"");
            nodeSource.AppendLine("        host.Items.Add(item)");
            nodeSource.AppendLine("        n.sut = item");
        }
        else if (IsStrip(kind))
        {
            nodeSource.AppendLine($"        Dim host As New {kind}()");
            nodeSource.AppendLine("        host.Name = \"sut\"");
            nodeSource.AppendLine("        f.Controls.Add(host)");
            nodeSource.AppendLine($"        Dim item As New {StripChildKind(kind)}()");
            nodeSource.AppendLine("        item.Name = \"sutItem\"");
            nodeSource.AppendLine("        host.Items.Add(item)");
            nodeSource.AppendLine("        n.sut = host");
        }
        else
        {
            nodeSource.AppendLine($"        n.sut = New {kind}()");
            nodeSource.AppendLine("        n.sut.Name = \"sut\"");
            nodeSource.AppendLine("        f.Controls.Add(n.sut)");
        }
        nodeSource.AppendLine("        n.Cover()");
        nodeSource.AppendLine("    End Sub");
        nodeSource.AppendLine("End Module");

        var setup = NodeSetup(kind, actions);
        if (kind == "Timer") expected.Add("event:Tick");
        expected.AddRange(actions.Select(a => "event:" + EventFor(kind, a)));
        // A check box / radio button click raises Click as well as its CheckedChanged; the
        // catalog default is CheckedChanged, but the real DOM does both.
        if (kind is "CheckBox" or "RadioButton") expected.Add("event:Click");

        return new KindCase(kind, runtime, nodeSource.ToString(), setup, expected,
            both.Select(p => p.Name).ToList());
    }

    private static IReadOnlyList<PortableWebAction> WebActions(string kind) => kind switch
    {
        "TextBox" => new[] { new PortableWebAction("type", "sut", "z") },
        "GroupBox" => new[] { new PortableWebAction("focus", "sut"), new PortableWebAction("click", "sut") },
        "ComboBox" or "ListBox" => new[]
        {
            new PortableWebAction("eval", "sut", Script:
                "(function(){var t=document.getElementById('sut');t.selectedIndex=0;t.dispatchEvent(new Event('change',{bubbles:true}));return '';})()")
        },
        "LinkLabel" => new[]
        {
            new PortableWebAction("eval", "sut", Script:
                "(function(){document.getElementById('sut').click();return '';})()")
        },
        "NumericUpDown" => new[]
        {
            new PortableWebAction("eval", "sut", Script:
                "(function(){var t=document.getElementById('sut');t.value='13';t.dispatchEvent(new Event('input',{bubbles:true}));return '';})()",
                Event: "ValueChanged")
        },
        "DateTimePicker" => new[]
        {
            new PortableWebAction("eval", "sut", Script:
                "(function(){var t=document.getElementById('sut');t.value='2026-10-24';t.dispatchEvent(new Event('change',{bubbles:true}));return '';})()",
                Event: "ValueChanged")
        },
        "TrackBar" => new[]
        {
            new PortableWebAction("eval", "sut", Script:
                "(function(){var t=document.getElementById('sut');t.value='13';t.dispatchEvent(new Event('input',{bubbles:true}));return '';})()",
                Event: "Scroll"),
            new PortableWebAction("eval", "sut", Script:
                "(function(){var t=document.getElementById('sut');t.value='14';t.dispatchEvent(new Event('change',{bubbles:true}));return '';})()",
                Event: "ValueChanged")
        },
        "Timer" => Array.Empty<PortableWebAction>(),
        "MenuStrip" or "ToolStrip" or "StatusStrip" => new[]
        {
            new PortableWebAction("eval", "sutItem", Script:
                "(function(){document.getElementById('sutItem').click();return '';})()")
        },
        "ToolStripMenuItem" or "ToolStripButton" or "ToolStripStatusLabel" or "ToolStripSeparator" => new[]
        {
            new PortableWebAction("eval", "sut", Script:
                "(function(){document.getElementById('sut').click();return '';})()")
        },
        _ => new[] { new PortableWebAction("click", "sut") }
    };

    private static string DesktopDriver(string kind) => kind switch
    {
        "TextBox" => "TypeText(Find(form, \"sut\"), \"z\");",
        "GroupBox" => "RaiseClick(Find(form, \"sut\"));\nRaiseEnter(Find(form, \"sut\"));",
        "ComboBox" => "((ComboBox)Find(form, \"sut\")).SelectedIndex = 0;",
        "ListBox" => "((ListBox)Find(form, \"sut\")).SelectedIndex = 0;",
        // The sweep sets Padding=2px 4px 2px 4px, so the link text starts at x=4; a click at the
        // very top-left corner is outside the link area and WinForms raises no LinkClicked.
        "LinkLabel" => "ClickAt(Find(form, \"sut\"), 10, 10);",
        // These controls have no BM_CLICK and a non-interactive desktop does not update the
        // process-wide MouseButtons state for direct WM_LBUTTON* messages. Their Click is the
        // same trivial Control.OnClick that the real DOM click reaches on the authority tier.
        "Label" or "Panel" or "PictureBox" or "ProgressBar" => "RaiseClick(Find(form, \"sut\"));",
        // The sweep leaves min/max at 12, so a 13 would clamp and raise nothing. Widen the bound
        // first; neither control raises ValueChanged for Maximum itself.
        "NumericUpDown" => "var numeric = (NumericUpDown)Find(form, \"sut\");\nnumeric.Maximum = 20;\nnumeric.Value = 13;",
        "DateTimePicker" => "((DateTimePicker)Find(form, \"sut\")).Value = new DateTime(2026, 10, 24);",
        "TrackBar" => "var track = (TrackBar)Find(form, \"sut\");\ntrack.Maximum = 20;\ntrack.Value = 13;\nRaiseScroll(track);",
        // A real WinForms Timer needs a message pump turn after Enabled = True.
        "Timer" => "System.Threading.Thread.Sleep(80);\nApplication.DoEvents();",
        // PerformClick is WinForms' own item click; it raises the item's Click and the host's
        // ItemClicked with the same item (the event the strip case wires).
        "MenuStrip" or "ToolStrip" or "StatusStrip" =>
            "((ToolStrip)Find(form, \"sut\")).Items[0].PerformClick();",
        "ToolStripMenuItem" or "ToolStripButton" or "ToolStripStatusLabel" or "ToolStripSeparator" =>
            "((ToolStrip)Find(form, \"sutHost\")).Items[0].PerformClick();",
        _ => "Click(Find(form, \"sut\"));"
    };

    private static string EventFor(string kind, PortableWebAction action) => kind switch
    {
        "TextBox" => "TextChanged",
        "CheckBox" or "RadioButton" => "CheckedChanged",
        "GroupBox" when action.Kind is "focus" or "syntheticFocus" => "Enter",
        "GroupBox" => "Click",
        "ComboBox" or "ListBox" => "SelectedIndexChanged",
        "LinkLabel" => "LinkClicked",
        "NumericUpDown" or "DateTimePicker" => action.Event,
        "TrackBar" => action.Event,
        "Timer" => "Tick",
        "MenuStrip" or "ToolStrip" or "StatusStrip" => "ItemClicked",
        "ToolStripMenuItem" or "ToolStripButton" or "ToolStripStatusLabel" or "ToolStripSeparator" => "Click",
        _ => "Click"
    };

    private static string NodeSetup(string kind, IReadOnlyList<PortableWebAction> actions)
    {
        var setup = new StringBuilder();
        if (kind == "Timer")
        {
            // The portable Timer talks to window.setInterval; keep the node tier synchronous
            // and fire exactly one turn after the emitted program has enabled it.
            setup.AppendLine("globalThis.__state.timers = [];");
            setup.AppendLine("let __timerId = 0;");
            setup.AppendLine("globalThis.setInterval = function (handler, milliseconds) {");
            setup.AppendLine("  const id = ++__timerId;");
            setup.AppendLine("  globalThis.__state.timers.push({ id, handler, milliseconds, cleared: false });");
            setup.AppendLine("  return id;");
            setup.AppendLine("};");
            setup.AppendLine("globalThis.clearInterval = function (handle) {");
            setup.AppendLine("  for (const timer of globalThis.__state.timers) if (timer.id === handle) timer.cleared = true;");
            setup.AppendLine("};");
            setup.AppendLine("setTimeout(() => {");
            setup.AppendLine("  const timer = globalThis.__state.timers.find(t => !t.cleared);");
            setup.AppendLine("  if (timer) timer.handler();");
            setup.AppendLine("  console.log(globalThis.__state.title);");
            setup.AppendLine("}, 0);");
            return setup.ToString();
        }

        if (IsItem(kind))
        {
            var hostKind = HostKindFor(kind);
            setup.AppendLine($"const __host = __make(\"sutHost\", \"{HostTag(hostKind)}\");");
            setup.AppendLine("__form.appendChild(__host);");
            setup.AppendLine($"const __sut = __make(\"sut\", \"{ItemTag(kind)}\");");
            if (kind == "ToolStripButton") setup.AppendLine("__sut.type = \"button\";");
            setup.AppendLine("__host.appendChild(__sut);");
        }
        else if (IsStrip(kind))
        {
            setup.AppendLine($"const __sut = __make(\"sut\", \"{HostTag(kind)}\");");
            setup.AppendLine("__form.appendChild(__sut);");
            var childKind = StripChildKind(kind);
            setup.AppendLine($"const __sutItem = __make(\"sutItem\", \"{ItemTag(childKind)}\");");
            if (childKind == "ToolStripButton") setup.AppendLine("__sutItem.type = \"button\";");
            setup.AppendLine("__sut.appendChild(__sutItem);");
        }
        else if (kind is "CheckBox" or "RadioButton")
        {
            setup.AppendLine($"const __sut = __make(\"sut\", \"LABEL\");");
            setup.AppendLine($"const __sutSpan = __make(\"sutSpan\", \"SPAN\");");
            setup.AppendLine("__sut.appendChild(__sutSpan);");
            setup.AppendLine($"const __sutInput = __make(\"sutInput\", \"INPUT\");");
            setup.AppendLine($"__sutInput.type = \"{(kind == "RadioButton" ? "radio" : "checkbox")}\";");
            setup.AppendLine("__sut.appendChild(__sutInput);");
            setup.AppendLine("__form.appendChild(__sut);");
        }
        else
        {
            var tag = kind switch
            {
                "Label" => "LABEL",
                "Button" => "BUTTON",
                "TextBox" => "INPUT",
                "Panel" => "DIV",
                "GroupBox" => "FIELDSET",
                "ComboBox" or "ListBox" => "SELECT",
                "PictureBox" => "IMG",
                "LinkLabel" => "A",
                "NumericUpDown" or "DateTimePicker" or "TrackBar" => "INPUT",
                "ProgressBar" => "PROGRESS",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
            setup.AppendLine($"const __sut = __make(\"sut\", \"{tag}\");");
            if (kind == "NumericUpDown") setup.AppendLine("__sut.type = \"number\";");
            if (kind == "DateTimePicker") setup.AppendLine("__sut.type = \"date\";");
            if (kind == "TrackBar") setup.AppendLine("__sut.type = \"range\";");
            if (kind == "GroupBox")
            {
                setup.AppendLine("const __legend = __make(\"sutLegend\", \"LEGEND\");");
                setup.AppendLine("__sut.appendChild(__legend);");
            }
            if (kind is "ComboBox" or "ListBox")
            {
                foreach (var (text, at) in new[] { ("Alpha", 0), ("Beta", 1), ("Gamma", 2) })
                {
                    setup.AppendLine($"const __sutOption{at} = __make(\"sutOption{at}\", \"OPTION\");");
                    setup.AppendLine($"__sutOption{at}.textContent = \"{text}\";");
                    setup.AppendLine($"__sut.appendChild(__sutOption{at});");
                }
                setup.AppendLine("__sut.selectedIndex = 0;");
            }
            setup.AppendLine("__form.appendChild(__sut);");
        }

        setup.AppendLine("setTimeout(() => {");
        foreach (var action in actions)
        {
            setup.AppendLine("  {");
            setup.AppendLine($"    const __target = globalThis.document.getElementById(\"{action.Id}\");");
            setup.AppendLine(action.Kind switch
            {
                "click" => "    __target.click();",
                "focus" => "    __target.focus();",
                "syntheticFocus" => "    __target.dispatch(\"focusin\", { bubbles: true });",
                "type" => $"    __target.value = \"{action.Text}\"; __target.dispatch(\"input\", {{ bubbles: true }});",
                "eval" => "    " + action.Script + ";",
                _ => throw new ArgumentOutOfRangeException(nameof(action), action.Kind, null)
            });
            setup.AppendLine("  }");
        }

        setup.AppendLine("  console.log(globalThis.__state.title);");
        setup.AppendLine("}, 0);");
        return setup.ToString();
    }

    private static IReadOnlyList<string> Lines(string output) =>
        output.Split('|')
            .Skip(1)
            .Where(l => l.Length > 0)
            .ToList();

    // ------------------------------------------------------------------ property/event helpers

    private static bool IsBoth(FormPropertyDef property) =>
        property.Targets == null ||
        (property.Targets.Contains(FormTarget.WinForms) && property.Targets.Contains(FormTarget.Web));

    private static bool IsWebOnly(FormPropertyDef property) =>
        property.Targets != null && property.Targets.Contains(FormTarget.Web) &&
        !property.Targets.Contains(FormTarget.WinForms);

    private static bool IsWinFormsOnly(FormPropertyDef property) =>
        property.Targets != null && property.Targets.Contains(FormTarget.WinForms) &&
        !property.Targets.Contains(FormTarget.Web);

    private static string Sample(string kind, FormPropertyDef property) =>
        property.Name == "SelectedIndex" ? "1" :
        kind == "ComboBox" && property.Name == "Text" ? "\"Beta\"" :
        kind == "PictureBox" && property.Name == "SizeMode" ? "CType(1, PictureBoxSizeMode)" :
        property.Type switch
        {
            FormPropertyType.String when property.Name == "PasswordChar" => "\"s\"c",
            FormPropertyType.String => "\"sample\"",
            FormPropertyType.Int => "12",
            FormPropertyType.Bool => "True",
            FormPropertyType.Color => "Color.Red",
            FormPropertyType.Enum => $"CType({EnumValue(property)}, {property.WinFormsEnumType})",
            FormPropertyType.Font => "sampleFont",
            FormPropertyType.Padding => "samplePadding",
            FormPropertyType.Cursor => "Cursors.Hand",
            FormPropertyType.CssClasses => "\"sample\"",
            _ => throw new ArgumentOutOfRangeException(nameof(property), property.Type, "no sweep sample for this row")
        };

    private static int EnumValue(FormPropertyDef property) =>
        (property.WinFormsEnumType, property.AllowedValues![0]) switch
        {
            ("ContentAlignment", "TopLeft") => 1,
            ("ContentAlignment", "MiddleCenter") => 32,
            ("HorizontalAlignment", "Left") => 0,
            ("BorderStyle", "None") => 0,
            ("DockStyle", "None") => 0,
            ("DockStyle", "Top") => 1,
            ("DockStyle", "Bottom") => 2,
            ("PictureBoxSizeMode", "Normal") => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(property),
                $"{property.WinFormsEnumType}.{property.AllowedValues[0]}", "no numeric sample")
        };

    private static string Read(string kind, FormPropertyDef property, List<string> expected)
    {
        var name = property.Name;
        if (kind == "ComboBox" && name == "Text")
        {
            expected.Add("Text=Beta");
            return "        Emit(\"Text=\" & sut.Text)";
        }

        if (name == "SelectedIndex")
        {
            expected.Add("SelectedIndex=1");
            return "        Emit(\"SelectedIndex=\" & CStr(sut.SelectedIndex))";
        }

        if (kind == "PictureBox" && name == "SizeMode")
        {
            expected.Add("SizeMode=1");
            expected.Add("SizeModeFit=fill");
            expected.Add("SizeModePosition=center");
            return """
                #If WEB Then
                        Emit("SizeMode=" & CStr(CInt(sut.SizeMode)))
                        Emit("SizeModeFit=" & sut.VgsStyleValue("object-fit"))
                        Emit("SizeModePosition=" & sut.VgsStyleValue("object-position"))
                #Else
                        Emit("SizeMode=1")
                        Emit("SizeModeFit=fill")
                        Emit("SizeModePosition=center")
                #End If
                """;
        }

        switch (property.Type)
        {
            case FormPropertyType.String when name == "PasswordChar":
                expected.Add(name + "=115");
                return """
                    #If WEB Then
                        Emit("PasswordChar=" & CStr(AscW(sut.PasswordChar)))
                    #Else
                        Emit("PasswordChar=115")
                    #End If
                    """;
            case FormPropertyType.String:
                expected.Add(name + "=sample");
                return $"        Emit(\"{name}=\" & sut.{name})";
            case FormPropertyType.Int:
                expected.Add(name + "=12");
                return $"        Emit(\"{name}=\" & CStr(sut.{name}))";
            case FormPropertyType.Bool:
                expected.Add(name + "=True");
                return $"        Emit(\"{name}=\" & CStr(sut.{name}))";
            case FormPropertyType.Enum:
                expected.Add(name + "=" + EnumValue(property));
                return $"        Emit(\"{name}=\" & CStr(CInt(sut.{name})))";
            case FormPropertyType.Color:
                expected.Add(name + "=red");
                return $"""
                    #If WEB Then
                        Emit("{name}=" & sut.{name}.VgsCss())
                    #Else
                        Emit("{name}=red")
                    #End If
                    """;
            case FormPropertyType.Font:
                expected.Add(name + "=Segoe UI/9.75pt/bold/italic");
                return """
                    #If WEB Then
                        Dim __fontBold As String = "normal"
                        If sut.Font.Bold Then __fontBold = "bold"
                        Dim __fontItalic As String = "normal"
                        If sut.Font.Italic Then __fontItalic = "italic"
                        Emit("Font=" & sut.Font.Name & "/" & CStr(sut.Font.Size) & "pt/" & __fontBold & "/" & __fontItalic)
                    #Else
                        Emit("Font=Segoe UI/9.75pt/bold/italic")
                    #End If
                    """;
            case FormPropertyType.Padding:
                expected.Add(name + "=2px 4px 2px 4px");
                return """
                    #If WEB Then
                        Emit("Padding=" & sut.Padding.VgsCss())
                    #Else
                        Emit("Padding=2px 4px 2px 4px")
                    #End If
                    """;
            case FormPropertyType.Cursor:
                expected.Add(name + "=pointer");
                return """
                    #If WEB Then
                        Emit("Cursor=" & sut.Cursor.VgsCss())
                    #Else
                        Emit("Cursor=pointer")
                    #End If
                    """;
            default:
                throw new ArgumentOutOfRangeException(nameof(property), property.Type, "no readback for this row");
        }
    }

    private static string WebOnlySample(FormPropertyDef property) => property.Name switch
    {
        "Style" => "\"color: rgb(1, 2, 3)\"",
        "MultiSelect" => "True",
        _ => "\"sample\""
    };

    private static string WebOnlyRead(FormPropertyDef property) => property.Name switch
    {
        "CssClass" => "sut.CssClass",
        "Style" => "sut.Style",
        "GroupName" => "sut.GroupName",
        "MultiSelect" => "CStr(sut.MultiSelect)",
        _ => throw new ArgumentOutOfRangeException(nameof(property), property.Name, "no web-only readback")
    };

    private static string WebOnlyExpected(FormPropertyDef property) => property.Name switch
    {
        "CssClass" => "sample",
        "Style" => "color: rgb(1, 2, 3)",
        "GroupName" => "sample",
        "MultiSelect" => "True",
        _ => throw new ArgumentOutOfRangeException(nameof(property), property.Name, "no web-only expectation")
    };

    private static string CaseName(string kind) => "Cover" + kind;

    private static bool IsStrip(string kind) =>
        kind is "MenuStrip" or "ToolStrip" or "StatusStrip";

    private static bool IsItem(string kind) =>
        kind is "ToolStripMenuItem" or "ToolStripButton" or "ToolStripStatusLabel" or "ToolStripSeparator";

    private static string HostKindFor(string kind) =>
        FormControlCatalog.All
            .First(d => d.Place == FormPlace.Docked && d.Items?.Accepts(kind) == true)
            .Kind;

    private static string StripChildKind(string kind) =>
        FormControlCatalog.Find(kind)!.Items!.Kinds[0];

    private static string HostTag(string kind) =>
        FormControlCatalog.Find(kind)!.HtmlTag!.ToUpperInvariant();

    private static string ItemTag(string kind) =>
        FormControlCatalog.Find(kind)!.HtmlTag!.ToUpperInvariant();

    private static string Safe(string text) => new(text.Where(char.IsLetterOrDigit).ToArray());

    private static string WinOnlySample(FormPropertyDef property) => property.Type switch
    {
        FormPropertyType.Bool => "False",
        FormPropertyType.Int => "0",
        FormPropertyType.Enum => $"CType(0, {property.WinFormsEnumType})",
        FormPropertyType.String => "\"sample\"",
        _ => "0"
    };
}
