using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class Task43ConversionTests
{
    private static (FormDocument Document, string Code) Sample()
    {
        var scaffold = FormScaffolder.Create("ConvertMe", FormTarget.Web);
        var document = new FormDocument { Target = FormTarget.Web, Name = "ConvertMe" };
        var button = new FormControl { Kind = "Button", Id = "btnGo" };
        button.Binds.Add(new FormBind { Event = "Click", Handler = "btnGo_Click" });
        document.Controls.Add(button);
        var marker = "    ' <vgs:designer region=\"init\"";
        var at = scaffold.CodeText.IndexOf(marker, StringComparison.Ordinal);
        var handler = "    Private Sub btnGo_Click(e As DomEvent)\n" +
                      "        txtUser.value = \"x\"\n" +
                      "    End Sub\n\n";
        return (document, scaffold.CodeText.Insert(at, handler));
    }

    [Test]
    public void Convert_AddsPortableHeaderAndMarkerAndRewritesSimpleHandler()
    {
        var (document, code) = Sample();
        var result = FormCodeConversion.Convert(document, code);

        Assert.That(result.Text, Does.StartWith("Using System\nUsing System.Drawing\nUsing System.Windows.Forms\n"));
        Assert.That(result.Text, Does.Contain("Inherits Form"));
        Assert.That(result.Text, Does.Contain("style=\"portable\""));
        Assert.That(result.Text, Does.Contain("Sub btnGo_Click(sender As Object, e As EventArgs)"));
        Assert.That(result.Text, Does.Contain("txtUser.value = \"x\""));
        Assert.That(result.Diagnostics.Any(d => d.Code == DesignCodes.DomUsage && d.IsWarning), Is.True);
    }

    [Test]
    public void Convert_LeavesANonSimpleHandlerAndReportsIt()
    {
        var (document, code) = Sample();
        code = code.Replace("Private Sub btnGo_Click(e As DomEvent)", "Private Sub btnGo_Click(e As DomEvent, extra As Integer)");
        var result = FormCodeConversion.Convert(document, code);
        Assert.That(result.Text, Does.Contain("Sub btnGo_Click(e As DomEvent, extra As Integer)"));
        Assert.That(result.Diagnostics.Any(d => d.Code == DesignCodes.NonSimpleHandler && d.IsWarning), Is.True);
    }

    [Test]
    public void Convert_RefusesADifferentInherits()
    {
        var (document, code) = Sample();
        code = code.Replace("Public Class ConvertMe", "Public Class ConvertMe\n    Inherits Object");
        var result = FormCodeConversion.Convert(document, code);
        Assert.That(result.Text, Is.EqualTo(code));
        Assert.That(result.Diagnostics.Any(d => !d.IsWarning), Is.True);
    }
}
