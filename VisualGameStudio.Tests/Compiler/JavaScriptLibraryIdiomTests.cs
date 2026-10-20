using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.JavaScript;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec ┬º4.13 ΓÇö the idioms the portable library relies on, each RUN under node against a stub DOM (node's own
/// EventTarget), compiled through the real project route so dom-core.bli is included. If a backend change breaks an
/// idiom, this fails before the library does. M25 is pinned as REFUSED: a DOM listener must be declared ABOVE the
/// method that attaches it.
/// </summary>
[TestFixture]
[Category("Integration")]
public class JavaScriptLibraryIdiomTests
{
    private const string StubDom =
        "globalThis.__els = {};\n" +
        "globalThis.document = { getElementById(id) { return globalThis.__els[id] ??= Object.assign(new EventTarget(), " +
        "{ id, click() { this.dispatchEvent(new Event('click')); } }); } };\n" +
        "globalThis.window = globalThis;\n";

    private static (bool Ok, string Output) Run(string source)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bl-idiom-" + Path.GetRandomFileName())).FullName;
        try
        {
            var file = Path.Combine(dir, "Main.bas");
            File.WriteAllText(file, source);
            var r = new BasicCompiler(new CompilerOptions { TargetBackend = "javascript" }).CompileProjectFiles(new[] { file });
            if (r.HasErrors) return (false, string.Join(" | ", r.AllErrors.Select(e => e.Message)));
            var js = new JavaScriptCodeGenerator().Generate(r.CombinedIR!);
            return (true, FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(StubDom + js)));
        }
        finally { Directory.Delete(dir, true); }
    }

    private const string Listener =
        "Public Class Ctl\n Private _el As Element\n Private _hits As Integer = 0\n" +
        " Private Sub VgsOnDomClick(e As DomEvent)\n  _hits = _hits + 1\n  Console.WriteLine(\"dom click \" & _hits)\n End Sub\n" +
        " Public Sub Attach(id As String)\n  Dim doc As Document = ::document\n  _el = doc.getElementById(id)\n" +
        "  _el.addEventListener(\"click\", AddressOf Me.VgsOnDomClick)\n End Sub\nEnd Class\n" +
        "Sub Main()\n Dim c As New Ctl()\n c.Attach(\"b\")\n Dim doc As Document = ::document\n doc.getElementById(\"b\").click()\nEnd Sub\n";

    [Test] public void M24_AListenerDeclaredAbove_SeesMe() => Assert.That(Run(Listener), Is.EqualTo((true, "dom click 1")));

    [Test]
    public void M23_ALambdaCallingMe() => Assert.That(Run(
        "Public Class Ctl\n Private _el As Element\n Private Sub Hit()\n  Console.WriteLine(\"hit\")\n End Sub\n" +
        " Public Sub Attach(id As String)\n  Dim doc As Document = ::document\n  _el = doc.getElementById(id)\n" +
        "  _el.addEventListener(\"click\", Sub(e As DomEvent) Me.Hit())\n End Sub\nEnd Class\n" +
        "Sub Main()\n Dim c As New Ctl()\n c.Attach(\"b\")\n Dim doc As Document = ::document\n doc.getElementById(\"b\").click()\nEnd Sub\n"),
        Is.EqualTo((true, "hit")));

    [Test]
    public void M21_OptionalParameters() => Assert.That(Run(
        "Public Class C\n Public Function D(Optional p As String = \"x\", Optional n As Integer = 3) As String\n  Return p & n\n End Function\nEnd Class\n" +
        "Sub Main()\n Dim c As New C()\n Console.WriteLine(c.D())\n Console.WriteLine(c.D(\"y\"))\n Console.WriteLine(c.D(\"z\", 7))\nEnd Sub\n"),
        Is.EqualTo((true, "x3\ny3\nz7")));

    [Test]
    public void M1_RaiseThroughABaseOnX() => Assert.That(Run(
        "Public Class EventArgs2\nEnd Class\n" +
        "Public Class Control\n Public Event Click(sender As Object, e As EventArgs2)\n" +
        " Protected Overridable Sub OnClick(e As EventArgs2)\n  RaiseEvent Click(Me, e)\n End Sub\n" +
        " Public Sub PerformClick()\n  Me.OnClick(New EventArgs2())\n End Sub\nEnd Class\n" +
        "Public Class Button\n Inherits Control\nEnd Class\n" +
        "Public Class Frm\n Private b As Button\n Public Sub New()\n  b = New Button()\n  AddHandler b.Click, AddressOf Me.H\n  b.PerformClick()\n End Sub\n" +
        " Private Sub H(sender As Object, e As EventArgs2)\n  If sender Is b Then\n   Console.WriteLine(\"sender ok\")\n  Else\n   Console.WriteLine(\"wrong sender\")\n  End If\n End Sub\nEnd Class\n" +
        "Sub Main()\n Dim f As New Frm()\nEnd Sub\n"),
        Is.EqualTo((true, "sender ok")));

    /// <summary>M4 fixed (Task 13) ΓÇö the library's Text override calls through MyBase.</summary>
    [Test]
    public void M4_APropertyOverride_ThroughMyBase() => Assert.That(Run(
        "Public Class Control\n Private _t As String = \"\"\n Public Overridable Property Text As String\n  Get\n   Return _t\n  End Get\n" +
        "  Set(value As String)\n   _t = value\n  End Set\n End Property\nEnd Class\n" +
        "Public Class Label\n Inherits Control\n Public Overrides Property Text As String\n  Get\n   Return MyBase.Text\n  End Get\n" +
        "  Set(value As String)\n   MyBase.Text = \"[\" & value & \"]\"\n  End Set\n End Property\nEnd Class\n" +
        "Sub Main()\n Dim l As New Label()\n l.Text = \"hi\"\n Console.WriteLine(l.Text)\nEnd Sub\n"),
        Is.EqualTo((true, "[hi]")));

    /// <summary>M25 ΓÇö the erasure: a listener declared BELOW its use is refused (the library's ordering rule).</summary>
    [Test]
    public void M25_AListenerDeclaredBelow_IsRefused()
    {
        var (ok, output) = Run(
            "Public Class Ctl\n Private _el As Element\n Public Sub Attach(id As String)\n  Dim doc As Document = ::document\n" +
            "  _el = doc.getElementById(id)\n  _el.addEventListener(\"click\", AddressOf Me.VgsLater)\n End Sub\n" +
            " Private Sub VgsLater(e As DomEvent)\n End Sub\nEnd Class\nSub Main()\nEnd Sub\n");
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(output, Does.Contain("Action<DomEvent>"));
        });
    }
}
