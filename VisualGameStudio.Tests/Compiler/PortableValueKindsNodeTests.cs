using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 38's node tier for the four value controls. The recording DOM supplies the real
/// valueAsDate shape; these tests exercise the library, not a kind-specific branch.
/// </summary>
[TestFixture]
[Category("Integration")]
public class PortableValueKindsNodeTests
{
    internal static readonly string[] ValueKinds = { "NumericUpDown", "DateTimePicker", "TrackBar", "ProgressBar" };

    private const string Fixtures = """
        function __value(id, kind) {
            const tag = kind === "ProgressBar" ? "PROGRESS" : "INPUT";
            const root = __make(id, tag);
            root.className = "vgs-" + kind;
            if (tag === "INPUT") {
                root.type = kind === "DateTimePicker" ? "date"
                    : kind === "TrackBar" ? "range" : "number";
            }
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
                Dim scrolls As Integer
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
                Sub Scrolled(sender As Object, e As EventArgs)
                    If sender IsNot expected Then Throw New Exception("wrong sender")
                    scrolls = scrolls + 1
                End Sub
            End Module
            """;
        var setup = Fixtures + $"\n__value('sut', '{kind}');\n" + extraSetup;
        var result = PortableControlNodeTests.Run(source, setup);
        Assert.That(result.Ok, Is.True, result.Errors);
        Assert.That(result.Output, Is.EqualTo(output));
    }

    [TestCaseSource(nameof(ValueKinds))]
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
    public void NumericUpDown_ReadsAndWritesDecimalExactly_AndRaisesValueChangedPerInput() => Expect("NumericUpDown", """
        Console.WriteLine(CStr(c.Minimum) & ":" & CStr(c.Maximum) & ":" & CStr(c.Value) & ":" & CStr(c.Increment))
        c.Minimum = 1.10D
        c.Maximum = 2.20D
        c.Value = 1.25D
        c.Increment = 0.05D
        Console.WriteLine(CStr(c.Minimum) & ":" & CStr(c.Maximum) & ":" & CStr(c.Value) & ":" & CStr(c.Increment))
        Console.WriteLine(c.Element.getAttribute("min") & ":" & c.Element.getAttribute("max") & ":" & c.Element.value & ":" & c.Element.getAttribute("step"))
        AddHandler c.ValueChanged, AddressOf Changed
        c.Value = 1.25D
        Console.WriteLine("same=" & hits)
        c.Value = 1.250D
        Console.WriteLine("scale=" & hits & ":" & c.Element.value)
        c.Value = 1.30D
        Console.WriteLine("changed=" & hits)
        Dim raw As Object = c.Element
        raw.dispatch("input")
        raw.dispatch("input")
        raw.dispatch("change")
        Console.WriteLine("input=" & hits)
        Console.WriteLine(CStr(c.ReadOnly) & ":" & CInt(c.TextAlign))
        c.ReadOnly = True
        c.TextAlign = HorizontalAlignment.Right
        Console.WriteLine(CStr(c.ReadOnly) & ":" & CInt(c.TextAlign) & ":" & c.VgsStyleValue("text-align"))
        """, "0:100:0:1\n1.10:2.20:1.25:0.05\n1.10:2.20:1.25:0.05\nsame=0\nscale=0:1.250\nchanged=1\ninput=3\nFalse:0\nTrue:1:right");

    [Test]
    public void DateTimePicker_MapsValueToValueAsDate_AndRaisesOnRealChanges() => Expect("DateTimePicker", """
        Dim raw As Object = c.Element
        Console.WriteLine(CStr(raw.valueAsDate Is Nothing))
        raw.value = "2026-10-24"
        Dim first As DateTime = c.Value
        Console.WriteLine(raw.value & ":" & CStr(raw.valueAsDate Is Nothing))
        AddHandler c.ValueChanged, AddressOf Changed
        c.Value = first
        Console.WriteLine("same=" & hits & ":" & raw.value)
        raw.value = "2026-10-25"
        c.Value = first
        Console.WriteLine("changed=" & hits & ":" & raw.value)
        c.Value = first
        Console.WriteLine("again=" & hits)
        raw.dispatch("change")
        Console.WriteLine("dom=" & hits)
        """, "True\n2026-10-24:False\nsame=0:2026-10-24\nchanged=1:2026-10-24\nagain=1\ndom=2");

    [Test]
    public void TrackBar_ProgrammaticValueRaisesValueChangedOnly_AndDomEventsSplit() => Expect("TrackBar", """
        Console.WriteLine(c.Minimum & ":" & c.Maximum & ":" & c.Value)
        c.Minimum = 2
        c.Maximum = 20
        c.Value = 5
        Console.WriteLine(c.Element.getAttribute("min") & ":" & c.Element.getAttribute("max") & ":" & c.Element.value & ":" & c.Value)
        AddHandler c.ValueChanged, AddressOf Changed
        AddHandler c.Scroll, AddressOf Scrolled
        c.Value = 5
        Console.WriteLine("same=" & hits & ":" & scrolls)
        c.Value = 6
        Console.WriteLine("changed=" & hits & ":" & scrolls)
        Dim raw As Object = c.Element
        raw.dispatch("input")
        raw.dispatch("input")
        Console.WriteLine("input=" & hits & ":" & scrolls)
        raw.dispatch("change")
        Console.WriteLine("change=" & hits & ":" & scrolls)
        """, "0:10:0\n2:20:5:5\nsame=0:0\nchanged=1:0\ninput=1:2\nchange=2:2");

    [Test]
    public void ProgressBar_MapsMaximumAndValue() => Expect("ProgressBar", """
        Console.WriteLine(c.Maximum & ":" & c.Value)
        c.Maximum = 200
        c.Value = 25
        Console.WriteLine(c.Element.getAttribute("max") & ":" & c.Element.value & ":" & c.Maximum & ":" & c.Value)
        """, "100:0\n200:25:200:25");

    [Test]
    public void ProgressBar_MinimumIsRefusedOnTheWeb()
    {
        var source = """
            Using System
            Using System.Windows.Forms
            Module Program
                Sub Main()
                    Dim f As New Form()
                    Dim c As New ProgressBar()
                    c.Name = "sut"
                    f.Controls.Add(c)
                    c.Minimum = 1
                End Sub
            End Module
            """;
        var result = PortableControlNodeTests.Run(source, Fixtures + "\n__value('sut', 'ProgressBar');\n");
        Assert.That(result.Ok, Is.False);
        Assert.That(result.Errors, Does.Contain("Minimum"));
    }
}
