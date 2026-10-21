using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 31's first coverage slice through CompileProjectFiles and Node. The recording
/// DOM uses §7's portable shapes; Task 32 owns emitting them. Browser layout is not
/// inferred from this tier. Radio exclusion/label forwarding are DOM behaviour, not
/// direct calls to library raisers from the tests.
/// </summary>
[TestFixture]
[Category("Integration")]
public class PortableFirstKindsNodeTests
{
    private static readonly string[] FirstKinds =
        { "Button", "Label", "TextBox", "CheckBox", "RadioButton", "Panel", "GroupBox" };

    private const string Fixtures = """
        function __portable(id, kind, tag, parent = __form) {
            const root = __make(id, kind === "CheckBox" || kind === "RadioButton" ? "LABEL" : tag);
            root.className = "vgs-" + kind;
            parent.appendChild(root);
            if (kind === "CheckBox" || kind === "RadioButton") {
                const input = __make(id + "Input", "INPUT");
                input.type = kind === "CheckBox" ? "checkbox" : "radio";
                root.appendChild(input);
                root.appendChild(__make(id + "Caption", "SPAN"));
            }
            if (kind === "GroupBox") {
                root.appendChild(__make(id + "Legend", "LEGEND"));
                root.appendChild(__make(id + "Child", "BUTTON"));
            }
            return root;
        }
        """;

    private static void Expect(string kind, string body, string output, string extraSetup = "")
    {
        var source = $$"""
            Using System
            Using System.Windows.Forms
            Module Program
                Dim hits As Integer
                Dim clicks As Integer
                Dim keys As Integer
                Dim expected As Control
                Sub Main()
                    Dim f As New Form()
                    Dim c As New {{kind}}()
                    c.Name = "sut"
                    f.Controls.Add(c)
                    expected = c
                    {{body}}
                End Sub
                Sub Changed(sender As Object, e As EventArgs)
                    If sender IsNot expected Then Throw New Exception("wrong sender")
                    hits = hits + 1
                End Sub
                Sub Clicked(sender As Object, e As EventArgs)
                    If sender IsNot expected Then Throw New Exception("wrong sender")
                    clicks = clicks + 1
                End Sub
                Sub Keyed(sender As Object, e As KeyEventArgs)
                    If sender IsNot expected Then Throw New Exception("wrong sender")
                    keys = keys + 1
                End Sub
                Sub Record(sender As Object, e As EventArgs)
                    hits = hits + 1
                    Console.WriteLine(CType(sender, Control).Name)
                End Sub
            End Module
            """;
        var setup = Fixtures + $"\n__portable('sut', '{kind}', '{FormControlCatalog.Find(kind)!.HtmlTag}');\n" + extraSetup;
        var result = PortableControlNodeTests.Run(source, setup);
        Assert.That(result.Ok, Is.True, result.Errors);
        Assert.That(result.Output, Is.EqualTo(output));
    }

    private static IEnumerable<TestCaseData> WebEvents() =>
        FormControlCatalog.All.Where(d => FirstKinds.Contains(d.Kind))
            .SelectMany(d => FormEvents.WiredOn(d, FormTarget.Web)
                .Select(e => new TestCaseData(d.Kind, e.Name, FormEvents.NameOn(e, FormTarget.Web))
                    .SetName("CatalogEvent(" + d.Kind + "." + e.Name + ")")));

    [TestCaseSource(nameof(WebEvents))]
    public void CatalogWebEvent_RaisesOnceWithTheControlAsSender(string kind, string eventName, string domEvent)
    {
        var stimulus = eventName == "CheckedChanged" ? "c.Element.querySelector(\"input\").click()" :
            domEvent == "input" ? "raw.value = \"typed\"\n raw.dispatch(\"input\")" :
            domEvent == "focusin" ? "c.Element.focus()" : "c.Element.click()";
        Expect(kind, $"AddHandler c.{eventName}, AddressOf Changed\nDim raw As Object = c.Element\n" +
            stimulus + "\nConsole.WriteLine(hits)", "1");
    }

    private static IEnumerable<string> TextKinds() => FirstKinds
        .Where(kind => FormControlCatalog.Find(kind)!.Property("Text") != null);

    [TestCaseSource(nameof(TextKinds))]
    public void Text_IsLiteralAndChangeOnly_WithoutDestroyingCaptionChildren(string kind)
    {
        var part = kind is "CheckBox" or "RadioButton" ? "c.Element.querySelector(\"span\").textContent" :
            kind == "GroupBox" ? "c.Element.querySelector(\"legend\").textContent" :
            kind == "TextBox" ? "c.Element.value" : "c.Element.textContent";
        var child = kind is "CheckBox" or "RadioButton" ? "input" : kind == "GroupBox" ? "button" : null;
        Expect(kind, "AddHandler c.TextChanged, AddressOf Changed\n" +
            "c.Text = \"<b>x</b>\"\nc.Text = \"<b>x</b>\"\nConsole.WriteLine(c.Text)\n" +
            $"Console.WriteLine({part})\n" +
            "Console.WriteLine(\"changes=\" & hits)\nConsole.WriteLine(\"markup=\" & c.Element.innerHTML)\n" +
            "Console.WriteLine(\"bold=\" & CStr(c.Element.querySelector(\"b\") IsNot Nothing))\n" +
            (child == null ? "" : $"Console.WriteLine(CStr(c.Element.querySelector(\"{child}\") IsNot Nothing))"),
            "<b>x</b>\n<b>x</b>\nchanges=1\nmarkup=\nbold=False" + (child == null ? "" : "\nTrue"));
    }

    [Test]
    public void TextBox_MapsPropertiesAndOwnedMethodsToTheInput() => Expect("TextBox", """
        Console.WriteLine(c.MaxLength & ":" & CStr(c.ReadOnly) & ":" & CInt(c.TextAlign))
        c.MaxLength = 17
        c.ReadOnly = True
        c.PlaceholderText = "hint"
        c.TextAlign = HorizontalAlignment.Right
        c.PasswordChar = "*"c
        Dim raw As Object = c.Element
        Console.WriteLine(raw.maxLength & ":" & CStr(raw.readOnly) & ":" & raw.getAttribute("placeholder"))
        Console.WriteLine(raw.type & ":" & CStr(c.PasswordChar) & ":" & raw.style.textAlign)
        Console.WriteLine(c.MaxLength & ":" & CStr(c.ReadOnly) & ":" & c.PlaceholderText & ":" & CInt(c.TextAlign))
        AddHandler c.TextChanged, AddressOf Changed
        c.Text = "abc"
        c.AppendText("!")
        c.AppendText("")
        c.AppendText(Nothing)
        c.SelectAll()
        Console.WriteLine(c.Text & ":" & raw.selectCalls & ":" & raw.selectionEnd & ":" & hits)
        c.Clear()
        c.Clear()
        Console.WriteLine("empty=" & c.Text & ":changes=" & hits)
        c.PasswordChar = ChrW(0)
        Console.WriteLine(raw.type & ":" & CStr(AscW(c.PasswordChar) = 0))
        """, "32767:False:0\n17:True:hint\npassword:*:right\n17:True:hint:1\nabc!:1:4:2\nempty=:changes=3\ntext:True");

    [Test]
    public void TextBox_NegativeMaxLength_IsRefusedWithoutChangingTheInput() => Expect("TextBox", """
        c.MaxLength = 17
        Try
            c.MaxLength = -1
        Catch ex As ArgumentOutOfRangeException
            Console.WriteLine("negative")
        End Try
        Console.WriteLine(c.MaxLength)
        """, "negative\n17");

    [TestCase("CheckBox")]
    [TestCase("RadioButton")]
    public void ProgrammaticChecked_IsChangeOnlyAndUsesTheInnerInput(string kind) => Expect(kind, """
        AddHandler c.CheckedChanged, AddressOf Changed
        c.Checked = True
        c.Checked = True
        Console.WriteLine(CStr(c.Element.querySelector("input").checked) & ":" & hits)
        c.Checked = False
        c.Checked = False
        Console.WriteLine(CStr(c.Element.querySelector("input").checked) & ":" & hits)
        """, "True:1\nFalse:2");

    [TestCaseSource(nameof(FirstKinds))]
    public void CommonProperties_KeepTheirDomMappingsForEachKind(string kind) => Expect(kind, """
        c.ForeColor = Color.Red
        c.BackColor = Color.Black
        c.Cursor = Cursors.Hand
        Dim font As New Font()
        font.Name = "Arial"
        font.Size = 12
        c.Font = font
        Console.WriteLine(c.Element.style.color & ":" & c.Element.style.backgroundColor & ":" & c.Element.style.cursor)
        Console.WriteLine(c.Element.style.fontFamily & ":" & c.Element.style.fontSize)
        c.Enabled = False
        Console.WriteLine(CStr(c.Enabled))
        c.Enabled = True
        f.Enabled = False
        Console.WriteLine(CStr(c.Enabled))
        f.Enabled = True
        Console.WriteLine(CStr(c.Enabled))
        c.Hide()
        Console.WriteLine(CStr(c.Element.hidden))
        c.Show()
        f.Hide()
        Console.WriteLine(CStr(c.Visible))
        f.Show()
        Console.WriteLine(CStr(c.Visible))
        """, "red:black:pointer\nArial:12pt\nFalse\nFalse\nTrue\nTrue\nFalse\nTrue");

    [Test]
    public void GroupBox_CreatesOnlyItsOwnLegendAndKeepsExistingChildren() => Expect("GroupBox", """
        c.Text = "outer"
        Console.WriteLine(c.Text)
        Console.WriteLine(c.Element.firstElementChild.tagName)
        Console.WriteLine(c.Element.querySelector("button").id)
        Console.WriteLine(c.Element.querySelector("fieldset").querySelector("legend").textContent)
        """, "outer\nLEGEND\nsutChild\ninner",
        "__state.els.sutLegend.remove(); const nested = __portable('nested','GroupBox','FIELDSET', __state.els.sut);" +
        "nested.querySelector('legend').textContent = 'inner';\n");

    [TestCaseSource(nameof(FirstKinds))]
    public void UnattachedKindText_StillThrowsInvalidOperationException(string kind) => Expect(kind, $$"""
        Dim unattached As New {{kind}}()
        Try
            unattached.Text = "not yet"
        Catch ex As InvalidOperationException
            Console.WriteLine("unattached")
        End Try
        """, "unattached");

    [Test]
    public void TextBox_MultilinePreservesAttributesPositionAndSubscriptions_AndUnhooksOldElements() => Expect("TextBox", """
        c.Text = "live value"
        c.ReadOnly = True
        c.MaxLength = 37
        c.PasswordChar = "#"c
        c.Element.setAttribute("aria-label", "label")
        c.Element.setAttribute("data-future", "keep")
        c.Element.style.color = "red"
        AddHandler c.TextChanged, AddressOf Changed
        AddHandler c.Click, AddressOf Clicked
        AddHandler c.KeyDown, AddressOf Keyed
        Dim old As Object = c.Element
        c.Multiline = True
        c.Multiline = True
        Console.WriteLine(c.Element.tagName & ":" & c.Text & ":" & hits)
        Console.WriteLine(c.Element.getAttribute("data-future") & ":" & c.Element.getAttribute("aria-label"))
        Console.WriteLine(c.Element.id & ":" & c.Element.className & ":" & c.Element.style.color)
        Console.WriteLine(CStr(c.ReadOnly) & ":" & c.MaxLength & ":" & CStr(c.PasswordChar))
        Dim raw As Object = c.Element
        raw.dispatch("input")
        raw.click()
        raw.fire("keydown", 0, 0, 0, "a", "KeyA", False, False, False, "")
        old.dispatch("input")
        old.click()
        old.fire("keydown", 0, 0, 0, "a", "KeyA", False, False, False, "")
        c.Multiline = False
        Dim current As Object = c.Element
        Console.WriteLine(current.tagName & ":" & current.type & ":" & c.Text)
        current.dispatch("input")
        current.click()
        raw.dispatch("input")
        Console.WriteLine(hits & ":" & clicks & ":" & keys)
        Dim doc As Document = ::document
        Console.WriteLine(CStr(doc.getElementById("sut") Is c.Element) & ":" & f.Controls.Count)
        Console.WriteLine(c.Element.nextElementSibling.id)
        c.Visible = False
        c.Enabled = False
        c.Multiline = True
        Console.WriteLine(CStr(c.Element.hidden) & ":" & CStr(c.Element.disabled))
        f.Controls.Remove(c)
        current.dispatch("input")
        Console.WriteLine(hits)
        """, "TEXTAREA:live value:0\nkeep:label\nsut:vgs-TextBox:red\nTrue:37:#\nINPUT:password:live value\n2:2:1\nTrue:1\nafter\nTrue:True\n2",
        "__form.appendChild(__make('after', 'DIV'));\n");

    [TestCase("CheckBox", 2, false)]
    [TestCase("RadioButton", 1, true)]
    public void CaptionForwarding_RaisesOneClickAndOneChangePerStateTransition(string kind, int changes, bool final) =>
        Expect(kind, """
            AddHandler c.CheckedChanged, AddressOf Changed
            AddHandler c.Click, AddressOf Clicked
            c.Element.querySelector("span").click()
            Console.WriteLine(CStr(c.Checked) & ":" & hits & ":" & clicks)
            c.Element.querySelector("input").click()
            c.Checked = c.Checked
            Console.WriteLine(CStr(c.Checked) & ":" & hits & ":" & clicks)
            Dim oldInput As Object = c.Element.querySelector("input")
            f.Controls.Remove(c)
            oldInput.dispatch("change")
            oldInput.click()
            Console.WriteLine(hits & ":" & clicks)
            """, $"True:1:1\n{final}:{changes}:2\n{changes}:2");

    [Test]
    public void RadioGroup_NotifiesTheBrowserUnselectedPeer_AndHonoursContainerGroups() => Expect("RadioButton", """
        Dim b As New RadioButton()
        b.Name = "b"
        f.Controls.Add(b)
        Dim d As New RadioButton()
        d.Name = "d"
        f.Controls.Add(d)
        AddHandler c.CheckedChanged, AddressOf Record
        AddHandler b.CheckedChanged, AddressOf Record
        AddHandler d.CheckedChanged, AddressOf Record
        c.Checked = True
        c.Checked = True
        b.Checked = True
        Console.WriteLine("program=" & hits & ":" & CStr(c.Checked) & ":" & CStr(b.Checked))
        c.Element.querySelector("span").click()
        Console.WriteLine("native=" & hits & ":" & CStr(c.Checked) & ":" & CStr(b.Checked))
        d.Checked = True
        Console.WriteLine("separate=" & hits & ":" & CStr(c.Checked) & ":" & CStr(d.Checked))
        d.GroupName = c.GroupName
        Console.WriteLine("regroup=" & hits & ":" & CStr(c.Checked) & ":" & CStr(d.Checked))
        f.Controls.Remove(c)
        b.Checked = True
        Console.WriteLine("removed=" & hits & ":" & CStr(b.Checked) & ":" & CStr(d.Checked))
        """, "sut\nsut\nb\nprogram=3:False:True\nb\nsut\nnative=5:True:False\nd\nseparate=6:True:True\nsut\nregroup=7:False:True\nd\nb\nremoved=9:True:False",
        "__portable('b','RadioButton','INPUT'); __portable('d','RadioButton','INPUT', __panel);\n");

    [Test]
    public void Button_PerformClick_UsesEffectiveEnabledVisibleAndButtonSender() => Expect("Button", """
        AddHandler c.Click, AddressOf Clicked
        c.PerformClick()
        c.Enabled = False
        c.PerformClick()
        c.Enabled = True
        c.Visible = False
        c.PerformClick()
        c.Visible = True
        f.Enabled = False
        c.PerformClick()
        f.Enabled = True
        f.Visible = False
        c.PerformClick()
        f.Visible = True
        c.PerformClick()
        Console.WriteLine(clicks)
        """, "2");

    [TestCase("Label", 1)]
    [TestCase("Button", 32)]
    public void ContentAlignment_MapsHorizontalStyleAndRetainsTheFullValue(string kind, int initial) => Expect(kind, """
        Console.WriteLine(CInt(c.TextAlign))
        c.TextAlign = ContentAlignment.BottomRight
        Console.WriteLine(c.Element.style.textAlign & ":" & CInt(c.TextAlign))
        c.TextAlign = ContentAlignment.TopCenter
        Console.WriteLine(c.Element.style.textAlign & ":" & CInt(c.TextAlign))
        c.TextAlign = ContentAlignment.MiddleLeft
        Console.WriteLine(c.Element.style.textAlign & ":" & CInt(c.TextAlign))
        """, $"{initial}\nright:1024\ncenter:2\nleft:16");

    [Test]
    public void Panel_BorderAndAutoScroll_WriteThroughTheStyleSeam() => Expect("Panel", """
        Console.WriteLine(CInt(c.BorderStyle) & ":" & CStr(c.AutoScroll))
        c.BorderStyle = BorderStyle.FixedSingle
        Console.WriteLine(c.Element.style.border & ":" & CInt(c.BorderStyle))
        c.BorderStyle = BorderStyle.Fixed3D
        Console.WriteLine(c.Element.style.border & ":" & CInt(c.BorderStyle))
        c.BorderStyle = BorderStyle.None
        c.AutoScroll = True
        Console.WriteLine(c.Element.style.border & ":" & CStr(c.AutoScroll) & ":" & c.Element.style.overflow)
        c.AutoScroll = False
        Console.WriteLine(CStr(c.AutoScroll) & ":" & c.Element.style.overflow)
        """, "0:False\n1px solid:1\n2px inset:2\nnone:True:auto\nFalse:");

    [Test]
    public void PortableMetadata_FeedsTheLibraryClientAndEnableSeams() => Expect("CheckBox", """
        Console.WriteLine(c.VgsClientInset() & ":" & c.VgsDesignClientWidth() & ":" & c.VgsDesignClientHeight())
        Dim pad As Padding = c.VgsDockPadding()
        Console.WriteLine(pad.Left & ":" & pad.Top & ":" & pad.Right & ":" & pad.Bottom)
        c.Enabled = False
        Console.WriteLine(CStr(c.Element.querySelector("input").disabled))
        c.Enabled = True
        c.Focus()
        Dim raw As Object = c.Element.querySelector("input")
        Console.WriteLine(CStr(raw.focused))
        """, "1:198:98\n3:19:3:3\nTrue\nTrue",
        "__state.els.sut.setAttribute('data-vgs-inset','1'); __state.els.sut.setAttribute('data-vgs-cw','198');" +
        "__state.els.sut.setAttribute('data-vgs-ch','98'); __state.els.sut.setAttribute('data-vgs-pl','3');" +
        "__state.els.sut.setAttribute('data-vgs-pt','19'); __state.els.sut.setAttribute('data-vgs-pr','3');" +
        "__state.els.sut.setAttribute('data-vgs-pb','3'); __state.els.sut.setAttribute('data-vgs-enable-target','input');\n");

    [TestCase(false)]
    [TestCase(true)]
    [NonParallelizable]
    public async Task ShippingBuildRoutes_IncludeAndRunTheFirstKinds(bool ideRoute)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bl-first-kinds-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "Main.bas"), """
                Using System
                Using System.Windows.Forms
                Module Program
                    Dim button As Button
                    Dim text As TextBox
                    Dim label As Label
                    Sub Main()
                        Dim f As New Form()
                        button = New Button()
                        button.Name = "btn"
                        text = New TextBox()
                        text.Name = "sut"
                        label = New Label()
                        label.Name = "label"
                        f.Controls.Add(button)
                        f.Controls.Add(text)
                        f.Controls.Add(label)
                        text.Text = "hello"
                        text.AppendText("!")
                        text.Multiline = True
                        AddHandler button.Click, AddressOf Clicked
                        button.PerformClick()
                        Console.WriteLine(label.Text)
                    End Sub
                    Sub Clicked(sender As Object, e As EventArgs)
                        If sender IsNot button Then Throw New Exception("wrong sender")
                        label.Text = text.Text
                    End Sub
                End Module
                """);
            var projectPath = Path.Combine(dir, "Site.blproj");
            await File.WriteAllTextAsync(projectPath, """
                <Project><PropertyGroup><ProjectName>Site</ProjectName><AssemblyName>Site</AssemblyName>
                <TargetBackend>JavaScript</TargetBackend></PropertyGroup>
                <ItemGroup><Compile Include="Main.bas" /></ItemGroup></Project>
                """);
            string script;
            if (ideRoute)
            {
                var project = await new VisualGameStudio.ProjectSystem.Serialization.ProjectSerializer().LoadAsync(projectPath);
                var build = await new VisualGameStudio.ProjectSystem.Services.BuildService(
                    Moq.Mock.Of<VisualGameStudio.Core.Abstractions.Services.IOutputService>()).BuildProjectAsync(project);
                Assert.That(build.Success, Is.True, string.Join("\n", build.Errors));
                script = Path.Combine(build.OutputPath!, build.GeneratedFileName);
            }
            else
            {
                var (exit, stdout, stderr) = await CliTestHarness.RunCli(dir, "build", projectPath);
                Assert.That(exit, Is.Zero, stdout + "\n" + stderr);
                script = Directory.GetFiles(Path.Combine(dir, "bin"), "Site.js", SearchOption.AllDirectories).Single();
            }
            var run = PortableControlNodeTests.RunEmitted(await File.ReadAllTextAsync(script),
                Fixtures + "__portable('sut','TextBox','INPUT'); __portable('label','Label','LABEL');\n");
            Assert.That(run.Ok, Is.True, run.Errors);
            Assert.That(run.Output, Is.EqualTo("hello!"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
