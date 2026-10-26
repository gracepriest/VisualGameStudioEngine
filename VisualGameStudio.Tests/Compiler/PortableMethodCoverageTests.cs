using System.Text;
using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 35's method gate. Every row of <see cref="PortableMembers"/> is called from one generated
/// portable program. The program prints the member's observable effect; the desktop and Edge runs
/// must agree. Only the browser dialog's missing title is recorded as the spec's divergence.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class PortableMethodCoverageTests
{
    private static readonly Dictionary<string, MethodCase> Cases = new(StringComparer.Ordinal);

    private static string _work = "";
    private static IReadOnlyDictionary<string, string> _edge = new Dictionary<string, string>();
    private static IReadOnlyDictionary<string, string> _winForms = new Dictionary<string, string>();

    public static IEnumerable<TestCaseData> Rows() =>
        PortableMembers.Manifest.Select(row =>
        {
            var key = Key(row);
            return new TestCaseData(key) { TestName = "{m}(" + key + ")" };
        });

    [OneTimeSetUp]
    public void BuildAndRun()
    {
        PortableRuntimeHarnesses.RequireWeb();
        Assert.That(PortableMembers.Manifest, Has.Count.EqualTo(34),
            "the method gate reads the portable manifest, not a hand list");
        Assert.That(PortableMembers.Manifest.Select(Key), Is.Unique);

        _work = Path.Combine(Path.GetTempPath(), "bl-portable-methods-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_work);
        foreach (var row in PortableMembers.Manifest)
        {
            Cases[Key(row)] = Build(row);
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

    [TestCaseSource(nameof(Rows))]
    public void EveryManifestRow_HasTheSameObservableEffectOnBothTargets(string key)
    {
        Assert.That(Cases.ContainsKey(key), Is.True);
        var desktop = _winForms[key];
        var web = _edge[key];
        Assert.That(desktop, Is.Not.Empty, $"{key} printed no effect in WinForms");
        Assert.That(web, Is.EqualTo(desktop),
            $"{key} differs between WinForms and Edge.\n--- WinForms ---\n{desktop}\n--- Edge ---\n{web}");
    }

    [Test]
    public void MessageBox_Show_RecordsTheMissingBrowserTitleAsTheKnownDivergence()
    {
        var row = PortableMembers.Manifest.Single(r => r.DeclaringType == "MessageBox" && r.Name == "Show");
        Assert.That(row.SampleArguments, Is.EqualTo(new[] { "\"hello\"", "\"\"", "MessageBoxButtons.OK" }));
        // Known divergence (spec 11.3): a browser alert has no caption/title. The manifest's empty
        // caption means this sample does not expose it; the return mapping below is the behaviour asserted.
        var output = _edge[Key(row)];
        Assert.That(output, Is.EqualTo("answer=1"));
    }

    // ------------------------------------------------------------------ case construction

    private sealed record MethodCase(string Key, PortableRuntimeCase Runtime);

    private static string Key(PortableMember row) => "Method" + row.DeclaringType + row.Name;

    private static MethodCase Build(PortableMember row)
    {
        var name = Key(row);
        var document = new FormDocument
        {
            Target = FormTarget.Web,
            Name = name,
            Width = 800,
            Height = 450,
            Text = name,
            Layout = new FormLayout { Kind = FormLayoutKind.Canvas }
        };

        var button = FormControlCatalog.All.First(d => d.Kind == "Button");
        var textBox = FormControlCatalog.All.First(d => d.Kind == "TextBox");
        var other = FormCatalogShapes.Canonical(document, button, "btnOther",
            geometry: new PixelGeometry { X = 40, Y = 40, Width = 120, Height = 32, Anchor = "Top, Left" });
        other.Properties["Text"] = "other";
        var sample = FormCatalogShapes.Canonical(document, button, "btnSample",
            geometry: new PixelGeometry { X = 40, Y = 90, Width = 120, Height = 32, Anchor = "Top, Left" });
        sample.Properties["Text"] = "sample";
        var text = FormCatalogShapes.Canonical(document, textBox, "txtSample",
            geometry: new PixelGeometry { X = 40, Y = 140, Width = 220, Height = 32, Anchor = "Top, Left" });
        text.Properties["Text"] = "";
        var combo = FormCatalogShapes.Canonical(document, FormControlCatalog.All.First(d => d.Kind == "ComboBox"),
            "cmbSample", geometry: new PixelGeometry { X = 40, Y = 190, Width = 220, Height = 32, Anchor = "Top, Left" });
        combo.Properties["Items"] = "Alpha, Beta, Gamma";
        combo.Properties["SelectedIndex"] = "0";
        var list = FormCatalogShapes.Canonical(document, FormControlCatalog.All.First(d => d.Kind == "ListBox"),
            "lstSample", geometry: new PixelGeometry { X = 40, Y = 240, Width = 220, Height = 60, Anchor = "Top, Left" });
        list.Properties["Items"] = "Alpha, Beta, Gamma";
        list.Properties["SelectedIndex"] = "0";

        // Task 39 method rows need a real strip with three live items and a Timer component.
        var menu = new FormControl { Kind = "MenuStrip", Id = "mnuSample" };
        menu.Properties["Dock"] = "Top";
        document.Controls.Add(menu);
        foreach (var (id, label) in new[] { ("itmAlpha", "Alpha"), ("itmBeta", "Beta"), ("itmGamma", "Gamma") })
        {
            var item = new FormControl { Kind = "ToolStripMenuItem", Id = id };
            item.Properties["Text"] = label;
            menu.Children.Add(item);
        }
        var timer = new FormControl { Kind = "Timer", Id = "tmrSample" };
        timer.Properties["Interval"] = "1000";
        document.Components.Add(timer);

        var body = new StringBuilder();
        body.AppendLine("    Private _textChanges As Integer");
        body.AppendLine("    Private _clicks As Integer");
        body.AppendLine("    Private _entered As Integer");
        body.AppendLine();
        body.AppendLine("    Public Sub Cover()");
        // A portable form's WinForms initializer deliberately carries no control properties. Edge
        // gets the options from the page markup; the desktop needs the same starting collection.
        body.AppendLine("#If WEB Then");
        body.AppendLine("        ' The page markup already has Alpha, Beta and Gamma.");
        body.AppendLine("#Else");
        body.AppendLine("        cmbSample.Items.Add(\"Alpha\")");
        body.AppendLine("        cmbSample.Items.Add(\"Beta\")");
        body.AppendLine("        cmbSample.Items.Add(\"Gamma\")");
        body.AppendLine("        cmbSample.SelectedIndex = 0");
        body.AppendLine("        lstSample.Items.Add(\"Alpha\")");
        body.AppendLine("        lstSample.Items.Add(\"Beta\")");
        body.AppendLine("        lstSample.Items.Add(\"Gamma\")");
        body.AppendLine("        lstSample.SelectedIndex = 0");
        body.AppendLine("#End If");
        foreach (var line in CodeFor(row).Replace("\r\n", "\n").Split('\n'))
        {
            body.AppendLine("        " + line);
        }

        body.AppendLine("    End Sub");
        body.AppendLine();
        body.AppendLine("    Private Sub HandleClick(sender As Object, e As EventArgs)");
        body.AppendLine("        _clicks = _clicks + 1");
        body.AppendLine("    End Sub");
        body.AppendLine();
        body.AppendLine("    Private Sub HandleTextChanged(sender As Object, e As EventArgs)");
        body.AppendLine("        _textChanges = _textChanges + 1");
        body.AppendLine("    End Sub");
        body.AppendLine();
        body.AppendLine("    Private Sub HandleEnter(sender As Object, e As EventArgs)");
        body.AppendLine("        _entered = _entered + 1");
        body.AppendLine("    End Sub");
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
        var runtime = new PortableRuntimeCase(name, document, source,
            DesktopDialog: row.DeclaringType == "MessageBox",
            DesktopDialogButton: "OK");
        return new MethodCase(name, runtime);
    }

    /// <summary>
    /// The method call and its observable effect. Add/Remove's manifest sample is
    /// <c>New Control()</c>; the runtime substitute is the attached <c>btnSample</c>, because a
    /// control with no element is invalid on both targets.
    /// </summary>
    private static string CodeFor(PortableMember row) => (row.DeclaringType, row.Name) switch
    {
        ("Control", "Show") => """
            AddHandler btnSample.Click, AddressOf HandleClick
            btnSample.Hide()
            Emit("hidden=" & CStr(btnSample.Visible))
            btnSample.Show()
            Emit("shown=" & CStr(btnSample.Visible))
            """,
        ("Control", "Hide") => """
            AddHandler btnSample.Click, AddressOf HandleClick
            btnSample.Show()
            btnSample.Hide()
            Emit("visible=" & CStr(btnSample.Visible))
            """,
        ("Control", "Focus") => """
            btnOther.Focus()
            btnSample.Focus()
            #If WEB Then
            Dim doc As Document = ::document
            Emit("focused=" & doc.activeElement.id)
            #Else
            Emit("focused=" & Me.ActiveControl.Name)
            #End If
            """,
        ("Control", "BringToFront") => """
            btnSample.BringToFront()
            #If WEB Then
            Dim nextElement As Element = ::document.getElementById("btnSample").nextElementSibling
            Emit("front=" & CStr(nextElement Is Nothing))
            #Else
            Emit("front=" & CStr(Me.Controls.GetChildIndex(btnSample) = 0))
            #End If
            """,
        ("ControlCollection", "Add") => """
            Me.Controls.Add(btnSample)
            Emit("count=" & CStr(Me.Controls.Count))
            #If WEB Then
            Emit("last=" & Me.Controls.Item(Me.Controls.Count - 1).Name)
            #Else
            Dim last As Control = Nothing
            For Each c As Control In Me.Controls
                last = c
            Next
            Emit("last=" & last.Name)
            #End If
            """,
        ("ControlCollection", "Remove") => """
            Me.Controls.Remove(btnOther)
            Emit("count=" & CStr(Me.Controls.Count))
            """,
        ("ControlCollection", "Item") => """
            #If WEB Then
            Emit("item=" & Me.Controls.Item(0).Name)
            #Else
            Dim first As Control = Nothing
            For Each c As Control In Me.Controls
                If first Is Nothing Then
                    first = c
                End If
            Next
            Emit("item=" & first.Name)
            #End If
            """,
        ("ControlCollection", "Count") => """
            Emit("count=" & CStr(Me.Controls.Count))
            """,
        ("ObjectCollection", "Add") => """
            cmbSample.Items.Add("Delta")
            Emit("count=" & CStr(cmbSample.Items.Count))
            #If WEB Then
            Emit("last=" & CStr(cmbSample.Items.Item(cmbSample.Items.Count - 1)))
            #Else
            Dim last As Object = Nothing
            For Each item As Object In cmbSample.Items
                last = item
            Next
            Emit("last=" & CStr(last))
            #End If
            """,
        ("ObjectCollection", "Insert") => """
            cmbSample.Items.Insert(1, "Inserted")
            Emit("count=" & CStr(cmbSample.Items.Count))
            #If WEB Then
            Emit("at=" & CStr(cmbSample.Items.Item(1)) & ":" & CStr(cmbSample.Items.Item(2)))
            #Else
            Dim at As Integer = 0
            Dim first As Object = Nothing
            Dim second As Object = Nothing
            For Each item As Object In cmbSample.Items
                If at = 1 Then first = item
                If at = 2 Then second = item
                at = at + 1
            Next
            Emit("at=" & CStr(first) & ":" & CStr(second))
            #End If
            """,
        ("ObjectCollection", "Remove") => """
            cmbSample.Items.Remove("Beta")
            Emit("count=" & CStr(cmbSample.Items.Count))
            Emit("has=" & CStr(cmbSample.Items.Contains("Beta")))
            """,
        ("ObjectCollection", "RemoveAt") => """
            cmbSample.Items.RemoveAt(0)
            Emit("count=" & CStr(cmbSample.Items.Count))
            #If WEB Then
            Emit("first=" & CStr(cmbSample.Items.Item(0)))
            #Else
            Dim first As Object = Nothing
            For Each item As Object In cmbSample.Items
                If first Is Nothing Then first = item
            Next
            Emit("first=" & CStr(first))
            #End If
            """,
        ("ObjectCollection", "Clear") => """
            cmbSample.Items.Clear()
            Emit("count=" & CStr(cmbSample.Items.Count))
            """,
        ("ObjectCollection", "Count") => """
            Emit("count=" & CStr(cmbSample.Items.Count))
            """,
        ("ObjectCollection", "Item") => """
            #If WEB Then
            Emit("item=" & CStr(cmbSample.Items.Item(1)))
            #Else
            Dim at As Integer = 0
            Dim second As Object = Nothing
            For Each item As Object In cmbSample.Items
                If at = 1 Then second = item
                at = at + 1
            Next
            Emit("item=" & CStr(second))
            #End If
            """,
        ("ObjectCollection", "IndexOf") => """
            Emit("index=" & CStr(cmbSample.Items.IndexOf("Gamma")))
            """,
        ("ObjectCollection", "Contains") => """
            Emit("has=" & CStr(cmbSample.Items.Contains("Beta")))
            """,
        ("MessageBox", "Show") => $"""
            Dim answer As DialogResult = CType(MessageBox.Show({string.Join(", ", row.SampleArguments)}), DialogResult)
            Emit("answer=" & CStr(CInt(answer)))
            """,
        ("Form", "Close") => """
            Me.Close()
            Emit("visible=" & CStr(Me.Visible))
            """,
        ("TextBox", "Clear") => """
            txtSample.Text = "abc"
            AddHandler txtSample.TextChanged, AddressOf HandleTextChanged
            txtSample.Clear()
            Emit("text='" & txtSample.Text & "'")
            Emit("changes=" & CStr(_textChanges))
            """,
        ("TextBox", "AppendText") => $"""
            txtSample.Text = "a"
            AddHandler txtSample.TextChanged, AddressOf HandleTextChanged
            txtSample.AppendText({string.Join(", ", row.SampleArguments)})
            Emit("text=" & txtSample.Text)
            Emit("changes=" & CStr(_textChanges))
            """,
        ("TextBox", "SelectAll") => """
            txtSample.Text = "abc"
            txtSample.Focus()
            txtSample.SelectAll()
            #If WEB Then
            Dim el As Element = ::document.getElementById("txtSample")
            Emit("selection=" & CStr(el.selectionStart) & "/" & CStr(el.selectionEnd))
            #Else
            Emit("selection=" & CStr(txtSample.SelectionStart) & "/" & CStr(txtSample.SelectionLength))
            #End If
            """,
        ("Button", "PerformClick") => """
            AddHandler btnSample.Click, AddressOf HandleClick
            btnSample.PerformClick()
            Emit("clicks=" & CStr(_clicks))
            """,
        ("Timer", "Start") => """
            tmrSample.Interval = 1000
            tmrSample.Start()
            Emit("enabled=" & CStr(tmrSample.Enabled))
            tmrSample.Stop()
            Emit("stopped=" & CStr(tmrSample.Enabled))
            """,
        ("Timer", "Stop") => """
            tmrSample.Start()
            tmrSample.Stop()
            Emit("enabled=" & CStr(tmrSample.Enabled))
            """,
        // The generated form declares mnuSample as a FIELD. The portable compiler resolves an
        // inherited/direct collection property on a field to Object, so the web branch names the
        // concrete collection type. The desktop branch uses the real WinForms collection, whose
        // indexer is Items(i) - it has no Item method.
        ("ToolStripItemCollection", "Add") => """
            #If WEB Then
            Dim items As ToolStripItemCollection = CType(mnuSample.Items, ToolStripItemCollection)
            Dim item As ToolStripItem = CType(items.Item(0), ToolStripItem)
            items.Add(item)
            Emit("count=" & CStr(items.Count))
            Emit("last=" & CType(items.Item(items.Count - 1), ToolStripItem).Name)
            #Else
            Dim item As ToolStripItem = Nothing
            For Each candidate As ToolStripItem In mnuSample.Items
                If item Is Nothing Then item = candidate
            Next
            mnuSample.Items.Add(item)
            Emit("count=" & CStr(mnuSample.Items.Count))
            Dim last As ToolStripItem = Nothing
            For Each candidate2 As ToolStripItem In mnuSample.Items
                last = candidate2
            Next
            Emit("last=" & last.Name)
            #End If
            """,
        ("ToolStripItemCollection", "Insert") => """
            #If WEB Then
            Dim items As ToolStripItemCollection = CType(mnuSample.Items, ToolStripItemCollection)
            Dim item As ToolStripItem = CType(items.Item(0), ToolStripItem)
            items.Insert(1, item)
            Emit("count=" & CStr(items.Count))
            Emit("at=" & CType(items.Item(0), ToolStripItem).Name & ":" & CType(items.Item(1), ToolStripItem).Name)
            #Else
            Dim item As ToolStripItem = Nothing
            For Each candidate As ToolStripItem In mnuSample.Items
                If item Is Nothing Then item = candidate
            Next
            mnuSample.Items.Insert(1, item)
            Emit("count=" & CStr(mnuSample.Items.Count))
            Dim first As ToolStripItem = Nothing
            Dim second As ToolStripItem = Nothing
            Dim at As Integer = 0
            For Each candidate2 As ToolStripItem In mnuSample.Items
                If at = 0 Then first = candidate2
                If at = 1 Then second = candidate2
                at = at + 1
            Next
            Emit("at=" & first.Name & ":" & second.Name)
            #End If
            """,
        ("ToolStripItemCollection", "Remove") => """
            #If WEB Then
            Dim items As ToolStripItemCollection = CType(mnuSample.Items, ToolStripItemCollection)
            Dim item As ToolStripItem = CType(items.Item(1), ToolStripItem)
            items.Remove(item)
            Emit("count=" & CStr(items.Count))
            Emit("has=" & CStr(items.Contains(item)))
            #Else
            Dim item As ToolStripItem = Nothing
            Dim at As Integer = 0
            For Each candidate As ToolStripItem In mnuSample.Items
                If at = 1 Then item = candidate
                at = at + 1
            Next
            mnuSample.Items.Remove(item)
            Emit("count=" & CStr(mnuSample.Items.Count))
            Emit("has=" & CStr(mnuSample.Items.Contains(item)))
            #End If
            """,
        ("ToolStripItemCollection", "RemoveAt") => """
            #If WEB Then
            Dim items As ToolStripItemCollection = CType(mnuSample.Items, ToolStripItemCollection)
            items.RemoveAt(0)
            Emit("count=" & CStr(items.Count))
            Emit("first=" & CType(items.Item(0), ToolStripItem).Name)
            #Else
            mnuSample.Items.RemoveAt(0)
            Emit("count=" & CStr(mnuSample.Items.Count))
            Dim first As ToolStripItem = Nothing
            For Each candidate As ToolStripItem In mnuSample.Items
                If first Is Nothing Then first = candidate
            Next
            Emit("first=" & first.Name)
            #End If
            """,
        ("ToolStripItemCollection", "Clear") => """
            #If WEB Then
            Dim items As ToolStripItemCollection = CType(mnuSample.Items, ToolStripItemCollection)
            items.Clear()
            Emit("count=" & CStr(items.Count))
            #Else
            mnuSample.Items.Clear()
            Emit("count=" & CStr(mnuSample.Items.Count))
            #End If
            """,
        ("ToolStripItemCollection", "Count") => """
            #If WEB Then
            Dim items As ToolStripItemCollection = CType(mnuSample.Items, ToolStripItemCollection)
            Emit("count=" & CStr(items.Count))
            #Else
            Emit("count=" & CStr(mnuSample.Items.Count))
            #End If
            """,
        ("ToolStripItemCollection", "Item") => """
            #If WEB Then
            Dim items As ToolStripItemCollection = CType(mnuSample.Items, ToolStripItemCollection)
            Emit("item=" & CType(items.Item(1), ToolStripItem).Name)
            #Else
            Dim item As ToolStripItem = Nothing
            Dim at As Integer = 0
            For Each candidate As ToolStripItem In mnuSample.Items
                If at = 1 Then item = candidate
                at = at + 1
            Next
            Emit("item=" & item.Name)
            #End If
            """,
        ("ToolStripItemCollection", "IndexOf") => """
            #If WEB Then
            Dim items As ToolStripItemCollection = CType(mnuSample.Items, ToolStripItemCollection)
            Emit("index=" & CStr(items.IndexOf(CType(items.Item(1), ToolStripItem))))
            #Else
            Dim item As ToolStripItem = Nothing
            Dim at As Integer = 0
            For Each candidate As ToolStripItem In mnuSample.Items
                If at = 1 Then item = candidate
                at = at + 1
            Next
            Emit("index=" & CStr(mnuSample.Items.IndexOf(item)))
            #End If
            """,
        ("ToolStripItemCollection", "Contains") => """
            #If WEB Then
            Dim items As ToolStripItemCollection = CType(mnuSample.Items, ToolStripItemCollection)
            Emit("has=" & CStr(items.Contains(CType(items.Item(1), ToolStripItem))))
            #Else
            Dim item As ToolStripItem = Nothing
            Dim at As Integer = 0
            For Each candidate As ToolStripItem In mnuSample.Items
                If at = 1 Then item = candidate
                at = at + 1
            Next
            Emit("has=" & CStr(mnuSample.Items.Contains(item)))
            #End If
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(row), row.DeclaringType + "." + row.Name,
            "the method gate has no case for this manifest row")
    };
}
