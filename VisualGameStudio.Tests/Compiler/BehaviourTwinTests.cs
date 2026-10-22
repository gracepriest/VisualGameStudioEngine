using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 35's behaviour twin. A Canvas page (pixels) and its retargeted WinForms form run the same
/// scenario. The code-behinds are compared outside their designer regions before either is built,
/// then both are driven by real input and their values, rectangles and event logs are compared.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class BehaviourTwinTests
{
    private const string FormName = "BehaviourTwinForm";

    private static string _work = "";
    private static string _winForms = "";
    private static string _edge = "";

    [OneTimeSetUp]
    public void BuildAndRun()
    {
        PortableRuntimeHarnesses.RequireWeb();
        _work = Path.Combine(Path.GetTempPath(), "bl-behaviour-twin-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_work);

        var page = BuildCanvasPage();
        var desktopPair = FormRetarget.ConvertToPair(page, FormTarget.WinForms);
        Assert.That(desktopPair.Diagnostics.Where(d => d.Code == DesignCodes.RetargetLayoutCrossed), Is.Empty,
            "a Canvas page must cross to WinForms without a layout loss");

        var webScaffold = FormScaffolder.Create(FormName, FormTarget.Web, portable: true);
        var webWrite = RegionWriter.Write(
            webScaffold.CodeFileName, webScaffold.CodeText, page, webScaffold.DocumentFileName);
        Assert.That(webWrite.Refused, Is.False,
            string.Join("; ", webWrite.Diagnostics.Select(d => d.Format())));

        var webBase = webWrite.Text;
        var desktopBase = desktopPair.CodeText;
        Assert.That(OutsideRegions(webBase), Is.EqualTo(OutsideRegions(desktopBase)),
            "the non-region code-behind bytes must be identical before anything is appended or built");

        var scenario = Scenario();
        var webSource = BeforeEndClass(AddCoverHook(webBase), scenario);
        var desktopSource = BeforeEndClass(AddCoverHook(desktopBase), scenario);

        var runtime = new PortableRuntimeCase(
            FormName,
            page,
            webSource,
            DesktopDriver:
                "Click(Find(form, \"btnTwin\"));\n" +
                "TypeKeys(Find(form, \"txtTwin\"), \"a\");\n" +
                "SelectAllKeys(Find(form, \"txtTwin\"));\n" +
                "Click(Find(form, \"btnFinish\"));",
            WebActions: new[]
            {
                new PortableWebAction("click", "btnTwin"),
                new PortableWebAction("typeKeys", "txtTwin", "a"),
                new PortableWebAction("selectKeys", "txtTwin"),
                new PortableWebAction("click", "btnFinish")
            },
            DesktopSource: desktopSource,
            ViewportWidth: 760,
            ViewportHeight: 600);

        var site = PortableRuntimeHarnesses.BuildWebSite(_work, new[] { runtime });
        _edge = PortableRuntimeHarnesses.RunEdge(site, new[] { runtime })[FormName];
        _winForms = PortableRuntimeHarnesses.RunWinForms(_work, new[] { runtime })[FormName];
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        try { if (Directory.Exists(_work)) Directory.Delete(_work, recursive: true); } catch { /* best effort */ }
    }

    [Test]
    public void ValuesAreEqual_RectanglesWithinOnePixel_AndTheEventLogIsOrderedTheSame()
    {
        var desktop = Parse(_winForms);
        var web = Parse(_edge);

        Assert.That(desktop.Values.Keys, Is.EquivalentTo(web.Values.Keys));
        foreach (var key in desktop.Values.Keys)
        {
            Assert.That(web.Values[key], Is.EqualTo(desktop.Values[key]),
                $"value {key} differs between WinForms and Edge");
        }

        Assert.That(desktop.Rects.Keys, Is.EquivalentTo(web.Rects.Keys));
        foreach (var key in desktop.Rects.Keys)
        {
            var a = desktop.Rects[key];
            var b = web.Rects[key];
            for (var i = 0; i < 4; i++)
            {
                Assert.That(Math.Abs(a[i] - b[i]), Is.LessThanOrEqualTo(1),
                    $"rectangle {key} differs by more than 1 px: {string.Join(",", a)} vs {string.Join(",", b)}");
            }
        }

        Assert.That(web.Events, Is.EqualTo(desktop.Events), "the ordered event log differs");
        Assert.That(desktop.Values.Keys, Does.Contain("initial.txt.Text"));
        Assert.That(desktop.Rects.Keys, Does.Contain("window.anchoredTwin"));
        Assert.That(desktop.Events, Has.Some.Contains("btnTwin~Click~twin"));
        Assert.That(desktop.Events, Has.Some.Contains("txtTwin~TextChanged~text=aseed"));
        Assert.That(desktop.Events, Has.Some.Contains("txtTwin~KeyDown~key=A;ctrl=False"));
        Assert.That(desktop.Events, Has.Some.Contains("txtTwin~KeyPress~char=97"));
        Assert.That(desktop.Events, Has.Some.Contains("btnFinish~Click~finish"));
    }

    // ------------------------------------------------------------------ fixture

    private static FormDocument BuildCanvasPage()
    {
        var page = new FormDocument
        {
            Target = FormTarget.Web,
            Name = FormName,
            Width = 760,
            Height = 540,
            Text = "Behaviour twin",
            Layout = new FormLayout { Kind = FormLayoutKind.Canvas }
        };

        var button = FormControlCatalog.All.First(d => d.Kind == "Button");
        var textBox = FormControlCatalog.All.First(d => d.Kind == "TextBox");
        var check = FormControlCatalog.All.First(d => d.Kind == "CheckBox");
        var label = FormControlCatalog.All.First(d => d.Kind == "Label");
        var panel = FormControlCatalog.All.First(d => d.Kind == "Panel");

        var twin = FormCatalogShapes.Canonical(page, button, "btnTwin",
            geometry: new PixelGeometry { X = 40, Y = 40, Width = 140, Height = 32, Anchor = "Top, Left" });
        twin.Properties["Text"] = "Twin";
        var text = FormCatalogShapes.Canonical(page, textBox, "txtTwin",
            geometry: new PixelGeometry { X = 40, Y = 80, Width = 220, Height = 23, Anchor = "Top, Left" });
        text.Properties["Text"] = "";
        var checkedBox = FormCatalogShapes.Canonical(page, check, "chkTwin",
            geometry: new PixelGeometry { X = 40, Y = 120, Width = 140, Height = 24, Anchor = "Top, Left" });
        checkedBox.Properties["Text"] = "check";
        var caption = FormCatalogShapes.Canonical(page, label, "lblTwin",
            geometry: new PixelGeometry { X = 40, Y = 152, Width = 180, Height = 24, Anchor = "Top, Left" });
        caption.Properties["Text"] = "caption";
        var anchored = FormCatalogShapes.Canonical(page, button, "anchoredTwin",
            geometry: new PixelGeometry { X = 520, Y = 40, Width = 160, Height = 32, Anchor = "Top, Right" });
        anchored.Properties["Text"] = "anchored";
        var finish = FormCatalogShapes.Canonical(page, button, "btnFinish",
            geometry: new PixelGeometry { X = 520, Y = 100, Width = 120, Height = 32, Anchor = "Top, Right" });
        finish.Properties["Text"] = "finish";

        var container = FormCatalogShapes.Canonical(page, panel, "panelTwin",
            geometry: new PixelGeometry { X = 40, Y = 200, Width = 400, Height = 160, Anchor = "Top, Left" });
        var docked = new FormControl
        {
            Kind = "Button",
            Id = "dockChild",
            TabIndex = 1,
            Geometry = new PixelGeometry { X = 0, Y = 0, Width = 400, Height = 32, Anchor = "Top, Left" }
        };
        docked.Properties["Text"] = "docked";
        container.Children.Add(docked);
        return page;
    }

    private static string AddCoverHook(string code)
    {
        const string anchor = "        Me.InitializeComponent()\n";
        Assert.That(code, Does.Contain(anchor), "the scaffold has no InitializeComponent call to hook");
        return code.Replace(anchor,
            anchor + "#If WEB Then\n            Me.Cover()\n#End If\n", StringComparison.Ordinal);
    }

    private static string BeforeEndClass(string text, string extra)
    {
        var at = text.LastIndexOf("End Class", StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThan(0));
        return text[..at] + extra.TrimEnd() + "\n\n" + text[at..];
    }

    private static string OutsideRegions(string code)
    {
        var lines = code.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var kept = new List<string>();
        var inside = false;
        foreach (var line in lines)
        {
            if (line.Contains("<vgs:designer", StringComparison.Ordinal)) { inside = true; continue; }
            if (line.Contains("</vgs:designer>", StringComparison.Ordinal)) { inside = false; continue; }
            if (!inside) kept.Add(line.TrimEnd());
        }

        return string.Join("\n", kept);
    }

    private static string Scenario() => """
    Private _log(100) As String
    Private _logCount As Integer

    Public Sub Cover()
        AddHandler txtTwin.TextChanged, AddressOf HandleTextChanged
        AddHandler txtTwin.KeyDown, AddressOf HandleKeyDown
        AddHandler txtTwin.KeyPress, AddressOf HandleKeyPress
        AddHandler txtTwin.KeyUp, AddressOf HandleKeyUp
        AddHandler btnTwin.Click, AddressOf HandleButtonClick
        AddHandler chkTwin.CheckedChanged, AddressOf HandleChecked
        AddHandler btnFinish.Click, AddressOf HandleFinish

        dockChild.Dock = DockStyle.Top
        ReflowWeb()
        Snapshot("initial")
        chkTwin.Checked = True
        Snapshot("checked")
        chkTwin.Visible = False
        Snapshot("hidden")
        chkTwin.Visible = True
        chkTwin.Enabled = False
        Snapshot("disabled")
        chkTwin.Enabled = True
        Snapshot("enabled")
        txtTwin.Text = "seed"
        ResetCaret()
        Snapshot("text")
        anchoredTwin.Left = 430
        Snapshot("anchored")
        dockChild.Dock = DockStyle.Bottom
        ReflowWeb()
        Snapshot("dock")
        panelTwin.Width = 360
        panelTwin.Height = 160
        ReflowWeb()
        Snapshot("panel")
        #If WEB Then
        Me.Width = 840
        Me.Height = 600
        #Else
        Me.ClientSize = New Size(840, 600)
        #End If
        ReflowWeb()
        Snapshot("window")
    End Sub

    Private Sub HandleButtonClick(sender As Object, e As EventArgs)
        LogEvent(sender, "Click", "twin")
        lblTwin.Text = "clicked"
    End Sub

    Private Sub HandleChecked(sender As Object, e As EventArgs)
        LogEvent(sender, "CheckedChanged", "checked=" & CStr(chkTwin.Checked))
    End Sub

    Private Sub HandleTextChanged(sender As Object, e As EventArgs)
        LogEvent(sender, "TextChanged", "text=" & txtTwin.Text)
    End Sub

    Private Sub HandleKeyDown(sender As Object, e As KeyEventArgs)
        Dim keyName As String = CStr(e.KeyCode)
        If keyName <> "ControlKey" AndAlso e.Control = False Then
            LogEvent(sender, "KeyDown", "key=" & keyName & ";ctrl=" & CStr(e.Control))
        End If
    End Sub

    Private Sub HandleKeyPress(sender As Object, e As KeyPressEventArgs)
        LogEvent(sender, "KeyPress", "char=" & CStr(AscW(e.KeyChar)))
    End Sub

    Private Sub HandleKeyUp(sender As Object, e As KeyEventArgs)
        Dim keyName As String = CStr(e.KeyCode)
        If keyName <> "ControlKey" AndAlso e.Control = False Then
            LogEvent(sender, "KeyUp", "key=" & keyName & ";ctrl=" & CStr(e.Control))
        End If
    End Sub

    Private Sub HandleFinish(sender As Object, e As EventArgs)
        LogEvent(sender, "Click", "finish")
        Snapshot("after")
        FlushLog()
    End Sub

    Private Sub LogEvent(sender As Object, eventName As String, detail As String)
        Dim c As Control = CType(sender, Control)
        If _logCount < 100 Then
            _log(_logCount) = c.Name & "~" & eventName & "~" & detail
            _logCount = _logCount + 1
        End If
    End Sub

    Private Sub FlushLog()
        For i As Integer = 0 To _logCount - 1
            Emit("E~" & CStr(i) & "~" & _log(i))
        Next
    End Sub

    Private Sub Snapshot(label As String)
        Emit("V~" & label & "~txt.Text~" & txtTwin.Text)
        Emit("V~" & label & "~lbl.Text~" & lblTwin.Text)
        Emit("V~" & label & "~chk.Visible~" & CStr(chkTwin.Visible))
        Emit("V~" & label & "~chk.Enabled~" & CStr(chkTwin.Enabled))
        Emit("V~" & label & "~chk.Checked~" & CStr(chkTwin.Checked))
        Emit("V~" & label & "~dock~" & CStr(CInt(dockChild.Dock)))
        Emit("V~" & label & "~selection~" & SelectionRange())
        Emit("R~" & label & "~btnTwin~" & Rect(btnTwin))
        Emit("R~" & label & "~txtTwin~" & Rect(txtTwin))
        Emit("R~" & label & "~lblTwin~" & Rect(lblTwin))
        Emit("R~" & label & "~panelTwin~" & Rect(panelTwin))
        Emit("R~" & label & "~dockChild~" & Rect(dockChild))
        Emit("R~" & label & "~anchoredTwin~" & Rect(anchoredTwin))
        Emit("R~" & label & "~btnFinish~" & Rect(btnFinish))
    End Sub

    Private Function Rect(c As Control) As String
        Return CStr(c.Left) & "~" & CStr(c.Top) & "~" & CStr(c.Width) & "~" & CStr(c.Height)
    End Function

    Private Function SelectionRange() As String
    #If WEB Then
        Dim el As Element = ::document.getElementById("txtTwin")
        Return CStr(el.selectionStart) & "/" & CStr(el.selectionEnd)
    #Else
        Return CStr(txtTwin.SelectionStart) & "/" & CStr(txtTwin.SelectionLength)
    #End If
    End Function

    Private Sub ResetCaret()
    #If WEB Then
        Dim el As Element = ::document.getElementById("txtTwin")
        el.selectionStart = 0
        el.selectionEnd = 0
    #Else
        txtTwin.SelectionStart = 0
        txtTwin.SelectionLength = 0
    #End If
    End Sub

    Private Sub ReflowWeb()
    #If WEB Then
        javascript{ if (window.vgsDock) window.vgsDock.reflow(); }
    #End If
    End Sub

    Private Sub Emit(line As String)
    #If WEB Then
        Dim doc As Document = ::document
        doc.title = doc.title & "|" & line
    #Else
        Console.WriteLine(line)
    #End If
    End Sub
    """;

    // ------------------------------------------------------------------ output

    private sealed record Parsed(
        Dictionary<string, string> Values,
        Dictionary<string, int[]> Rects,
        List<string> Events);

    private static Parsed Parse(string output)
    {
        var values = new Dictionary<string, string>();
        var rects = new Dictionary<string, int[]>();
        var events = new List<string>();
        foreach (var raw in output.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split('~');
            switch (parts[0])
            {
                case "V" when parts.Length >= 4:
                    values[parts[1] + "." + parts[2]] = parts[3];
                    break;
                case "R" when parts.Length >= 7:
                    rects[parts[1] + "." + parts[2]] = new[]
                    {
                        int.Parse(parts[3]), int.Parse(parts[4]), int.Parse(parts[5]), int.Parse(parts[6])
                    };
                    break;
                case "E" when parts.Length >= 4:
                    events.Add(string.Join("~", parts.Skip(2)));
                    break;
                default:
                    throw new InvalidDataException("unrecognised twin output line: " + line);
            }
        }

        return new Parsed(values, rects, events);
    }
}
