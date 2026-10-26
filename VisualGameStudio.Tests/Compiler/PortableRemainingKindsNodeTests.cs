using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 37's node tier for the four remaining positioned kinds. The recording DOM's
/// select/option behavior is real single-select behavior (see PortableControlNodeTests),
/// so these assertions exercise the library rather than a kind-specific test branch.
/// </summary>
[TestFixture]
[Category("Integration")]
public class PortableRemainingKindsNodeTests
{
    internal static readonly string[] RemainingKinds = { "ComboBox", "ListBox", "PictureBox", "LinkLabel" };

    private const string Fixtures = """
        function __remaining(id, kind) {
            const tag = kind === "ComboBox" || kind === "ListBox" ? "SELECT"
                : kind === "PictureBox" ? "IMG" : "A";
            const root = __make(id, tag);
            root.className = "vgs-" + kind;
            if (tag === "SELECT") {
                ["Alpha", "Beta", "Gamma"].forEach((text, at) => {
                    const option = __make(id + "Option" + at, "OPTION");
                    option.textContent = text;
                    root.appendChild(option);
                });
                root.selectedIndex = 0;
            }
            if (tag === "IMG") root.setAttribute("data-vgs-sizemode", "4");
            __form.appendChild(root);
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
                Dim links As Integer
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
                Sub Linked(sender As Object, e As LinkLabelLinkClickedEventArgs)
                    If sender IsNot expected Then Throw New Exception("wrong sender")
                    links = links + 1
                End Sub
            End Module
            """;
        var setup = Fixtures + $"\n__remaining('sut', '{kind}');\n" + extraSetup;
        var result = PortableControlNodeTests.Run(source, setup);
        Assert.That(result.Ok, Is.True, result.Errors);
        Assert.That(result.Output, Is.EqualTo(output));
    }

    [TestCaseSource(nameof(RemainingKinds))]
    public void CommonProperties_KeepTheirDomMappingsForEachKind(string kind) => Expect(kind, """
        c.ForeColor = Color.Red
        c.BackColor = Color.Black
        c.Cursor = Cursors.Hand
        Dim font As New Font()
        font.Name = "Arial"
        font.Size = 12
        c.Font = font
        Console.WriteLine(c.VgsStyleRule())
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
        """, "color: red;background-color: black;cursor: pointer;font-family: Arial;font-size: 12pt;font-weight: normal;font-style: normal;\nFalse\nFalse\nTrue\nTrue\nFalse\nTrue");

    [Test]
    public void ComboBox_TextIsTheSelectedOption_AndSelectionEventsAreChangeOnly() => Expect("ComboBox", """
        AddHandler c.SelectedIndexChanged, AddressOf Changed
        Console.WriteLine(c.Items.Count & ":" & c.SelectedIndex & ":" & c.Text)
        c.Text = "Gamma"
        Console.WriteLine(c.SelectedIndex & ":" & c.Text & ":" & hits)
        c.Text = "Gamma"
        c.Text = "missing"
        Console.WriteLine(c.SelectedIndex & ":[" & c.Text & "]:" & hits)
        c.SelectedIndex = 0
        c.SelectedIndex = 0
        Console.WriteLine(c.SelectedIndex & ":" & c.Text & ":" & hits)
        Dim raw As Object = c.Element
        raw.selectedIndex = 2
        raw.dispatch("change")
        raw.dispatch("change")
        Console.WriteLine("dom=" & c.SelectedIndex & ":" & hits)
        """, "3:0:Alpha\n2:Gamma:1\n-1:[]:2\n0:Alpha:3\ndom=2:4");

    [Test]
    public void ComboBox_StructuralItemEdits_PreserveTheSurvivingSelection_AndClearWhenItIsRemoved() => Expect("ComboBox", """
        AddHandler c.SelectedIndexChanged, AddressOf Changed
        c.SelectedIndex = 1
        c.Items.Insert(0, "Inserted")
        Console.WriteLine("insert=" & c.SelectedIndex & ":" & c.Text & ":" & hits)
        c.Items.Add("Delta")
        Console.WriteLine("add=" & c.SelectedIndex & ":" & c.Text & ":" & hits & ":" & c.Items.Count)
        c.Items.RemoveAt(0)
        Console.WriteLine("before=" & c.SelectedIndex & ":" & c.Text & ":" & hits)
        c.Items.RemoveAt(c.SelectedIndex)
        Console.WriteLine("remove=" & c.SelectedIndex & ":[" & c.Text & "]:" & hits & ":" & c.Items.Count)
        """, "insert=2:Beta:1\nadd=2:Beta:1:5\nbefore=1:Beta:1\nremove=-1:[]:2:3");

    [TestCase("ComboBox")]
    [TestCase("ListBox")]
    public void ObjectCollection_Members_MapToTheLiveOptions(string kind) => Expect(kind, """
        Console.WriteLine("count=" & c.Items.Count)
        Console.WriteLine("item=" & CStr(c.Items.Item(1)))
        Console.WriteLine("index=" & CStr(c.Items.IndexOf("Gamma")))
        Console.WriteLine("has=" & CStr(c.Items.Contains("Beta")))
        c.Items.Add("Delta")
        Console.WriteLine("add=" & c.Items.Count & ":" & CStr(c.Items.Item(3)))
        c.Items.Insert(1, "Inserted")
        Console.WriteLine("insert=" & c.Items.Count & ":" & CStr(c.Items.Item(1)) & ":" & CStr(c.Items.Item(2)))
        c.Items.Remove("Inserted")
        Console.WriteLine("remove=" & c.Items.Count & ":" & CStr(c.Items.Item(1)))
        c.Items.RemoveAt(0)
        Console.WriteLine("removeAt=" & c.Items.Count & ":" & CStr(c.Items.Item(0)))
        c.Items.Clear()
        Console.WriteLine("clear=" & c.Items.Count)
        """, "count=3\nitem=Beta\nindex=2\nhas=True\nadd=4:Delta\ninsert=5:Inserted:Beta\nremove=4:Beta\nremoveAt=3:Beta\nclear=0");

    [Test]
    public void ListBox_MultiSelect_IsTheNativeMultipleProperty() => Expect("ListBox", """
        Console.WriteLine(CStr(c.MultiSelect))
        c.MultiSelect = True
        Dim raw As Object = c.Element
        Console.WriteLine(CStr(raw.multiple) & ":" & CStr(c.MultiSelect))
        c.MultiSelect = False
        Console.WriteLine(CStr(raw.multiple) & ":" & CStr(c.MultiSelect))
        """, "False\nTrue:True\nFalse:False");

    [Test]
    public void PictureBox_ImageAndEverySizeMode_MapToSrcAndTheStyleRule() => Expect("PictureBox", """
        Console.WriteLine("mode=" & CInt(c.SizeMode))
        Console.WriteLine("[" & c.Image & "]")
        c.Image = "pic.png"
        Console.WriteLine(c.Image & ":" & c.Element.getAttribute("src"))
        c.SizeMode = PictureBoxSizeMode.CenterImage
        Console.WriteLine(CInt(c.SizeMode) & ":" & c.VgsStyleValue("object-fit") & ":" & c.VgsStyleValue("object-position"))
        c.SizeMode = PictureBoxSizeMode.Zoom
        Console.WriteLine(CInt(c.SizeMode) & ":" & c.VgsStyleValue("object-fit") & ":" & c.VgsStyleValue("object-position"))
        c.SizeMode = PictureBoxSizeMode.StretchImage
        Console.WriteLine(CInt(c.SizeMode) & ":" & c.VgsStyleValue("object-fit") & ":" & c.VgsStyleValue("object-position"))
        c.SizeMode = PictureBoxSizeMode.AutoSize
        Console.WriteLine(CInt(c.SizeMode) & ":" & c.VgsStyleValue("object-fit") & ":" & c.VgsStyleValue("object-position"))
        c.SizeMode = PictureBoxSizeMode.Normal
        Console.WriteLine(CInt(c.SizeMode) & ":" & c.VgsStyleValue("object-fit") & ":" & c.VgsStyleValue("object-position"))
        c.Image = ""
        Console.WriteLine("[" & c.Image & "]:" & CStr(c.Element.getAttribute("src") Is Nothing))
        """, "mode=4\n[]\npic.png:pic.png\n3:none:center\n4:contain:center\n1:fill:center\n2:none:left top\n0:none:left top\n[]:True");

    [Test]
    public void LinkLabel_PreventsNavigation_AndRaisesLinkClickedOnlyWhileEnabled() => Expect("LinkLabel", """
        AddHandler c.LinkClicked, AddressOf Linked
        AddHandler c.Click, AddressOf Clicked
        Dim raw As Object = c.Element
        Dim first As Object = raw.dispatch("click")
        Console.WriteLine(CStr(first.defaultPrevented) & ":" & links & ":" & clicks)
        c.Enabled = False
        Dim second As Object = raw.dispatch("click")
        Console.WriteLine(CStr(second.defaultPrevented) & ":" & links & ":" & clicks)
        """, "True:1:1\nTrue:1:1");
}
