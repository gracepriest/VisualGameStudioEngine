using System;
using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.JavaScript;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 34 — the four named web/desktop build errors and the LSP-facing library inclusion.
/// These drive <see cref="BasicCompiler"/> so the library's auto-inclusion hook and the
/// post-parse source checks are both in the measured path.
/// </summary>
[TestFixture]
public class PortableBuildErrorTests
{
    private static BasicCompiler Js() => new(new CompilerOptions { TargetBackend = "javascript" });

    private static BasicCompiler Cs() => new(new CompilerOptions { TargetBackend = "csharp" });

    private static CompilationResult Compile(
        BasicCompiler compiler, params (string Name, string Text)[] files)
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "bl-t34-" + Path.GetRandomFileName())).FullName;
        try
        {
            var paths = files
                .Select(f =>
                {
                    var path = Path.Combine(directory, f.Name);
                    File.WriteAllText(path, f.Text);
                    return path;
                })
                .ToArray();
            return compiler.CompileProjectFiles(paths);
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    private static string Generated(CompilationResult result) =>
        new JavaScriptCodeGenerator().Generate(result.CombinedIR);

    private static string Errors(CompilationResult result) =>
        string.Join(" | ", result.AllErrors.Select(e => e.Message));

    private const string PortableFormHead =
        "Using System\n" +
        "Using System.Windows.Forms\n\n" +
        "Public Class LoginForm\n" +
        "    Inherits Form\n" +
        "    Private btn As Button\n\n";

    // ------------------------------------------------------- WebUnavailableMember

    [Test]
    public void WebBuild_RefusesAMethodTheLibraryDoesNotDeclare()
    {
        var result = Compile(Js(), ("LoginForm.bas", PortableFormHead +
            "    Public Sub Test()\n" +
            "        btn.ShowDialog()\n" +
            "    End Sub\n" +
            "End Class\n"));

        Assert.That(result.Success, Is.False);
        Assert.That(Errors(result), Does.Contain("WebUnavailableMember"));
        Assert.That(Errors(result), Does.Contain("'Button.ShowDialog'"));
        Assert.That(Errors(result), Does.Contain("#If DESKTOP"));
        Assert.That(Errors(result), Does.Not.Contain("Available members"));
    }

    [Test]
    public void WebBuild_RefusesShowDialogThroughAFormSubclassReceiver()
    {
        var result = Compile(Js(), ("LoginForm.bas", PortableFormHead +
            "    Public Sub Test()\n" +
            "        Me.ShowDialog()\n" +
            "    End Sub\n" +
            "End Class\n"));

        Assert.That(result.Success, Is.False);
        Assert.That(Errors(result), Does.Contain("WebUnavailableMember: 'Form.ShowDialog'"));
    }

    [Test]
    public void WebBuild_RefusesAnUnavailableMemberInAWithBlock()
    {
        var result = Compile(Js(), ("LoginForm.bas", PortableFormHead +
            "    Public Sub Test()\n" +
            "        With btn\n" +
            "            .ShowDialog()\n" +
            "        End With\n" +
            "    End Sub\n" +
            "End Class\n"));

        Assert.That(result.Success, Is.False);
        Assert.That(Errors(result), Does.Contain("WebUnavailableMember: 'Button.ShowDialog'"));
    }

    [Test]
    public void WebBuild_RefusesAPropertyTheLibraryDoesNotDeclare()
    {
        var result = Compile(Js(), ("LoginForm.bas", PortableFormHead +
            "    Public Sub Test()\n" +
            "        btn.Nope = 1\n" +
            "    End Sub\n" +
            "End Class\n"));

        Assert.That(result.Success, Is.False);
        Assert.That(Errors(result), Does.Contain("WebUnavailableMember: 'Button.Nope'"));
        Assert.That(Errors(result), Does.Not.Contain("Available members"));
    }

    [Test]
    public void WebBuild_RefusesAMessageBoxButtonsMemberBeyondTheThree()
    {
        var result = Compile(Js(), ("LoginForm.bas", PortableFormHead +
            "    Public Sub Test()\n" +
            "        MessageBox.Show(\"x\", \"\", MessageBoxButtons.RetryCancel)\n" +
            "    End Sub\n" +
            "End Class\n"));

        Assert.That(result.Success, Is.False);
        Assert.That(Errors(result), Does.Contain("WebUnavailableMember: 'MessageBoxButtons.RetryCancel'"));
        Assert.That(Errors(result), Does.Contain("OK, OKCancel and YesNo"));
        Assert.That(Errors(result), Does.Not.Contain("cannot convert"));
    }

    [Test]
    public void WebBuild_RefusesADesktopOnlyWinFormsTypeByName()
    {
        var result = Compile(Js(), ("Main.bas",
            "Using System\nUsing System.Windows.Forms\n\n" +
            "Module Program\n    Sub Main()\n        Dim g As DataGridView\n    End Sub\nEnd Module\n"));

        var ex = Assert.Throws<ForeignFeatureException>(() => Generated(result));
        Assert.That(ex!.Message, Does.Contain("WebUnavailableMember"));
        Assert.That(ex.Message, Does.Contain("'DataGridView'"));
        Assert.That(ex.Message, Does.Contain("#If DESKTOP"));
        Assert.That(ex.Message, Does.Not.Contain("base class library"));
    }

    [Test]
    public void NonWinFormsBclTypes_KeepTheGenericBl7007()
    {
        var result = Compile(Js(), ("Main.bas",
            "Module Program\n    Sub Main()\n        Dim s As Stream\n    End Sub\nEnd Module\n"));

        var ex = Assert.Throws<ForeignFeatureException>(() => Generated(result));
        Assert.That(ex!.Message, Does.Contain("BL7007"));
        Assert.That(ex.Message, Does.Not.Contain("WebUnavailableMember"));
    }

    // ------------------------------------------------------- RuntimeControlCreation

    [Test]
    public void WebBuild_RefusesNewOfAControlOutsideTheInitRegion()
    {
        var result = Compile(Js(), ("Main.bas",
            "Using System\nUsing System.Windows.Forms\n\n" +
            "Module Program\n    Sub Main()\n        Dim b As New Button()\n    End Sub\nEnd Module\n"));

        Assert.That(result.Success, Is.False);
        Assert.That(Errors(result), Does.Contain(
            "RuntimeControlCreation: Creating a Button at run time is not supported on the web yet."));
    }

    [Test]
    public void WebBuild_AllowsNewOfAControlInsideTheInitRegion()
    {
        var result = Compile(Js(), ("LoginForm.bas",
            "Using System\nUsing System.Windows.Forms\n\n" +
            "Public Class LoginForm\n" +
            "    Private btn As Button\n\n" +
            "    ' <vgs:designer region=\"init\" form=\"LoginForm.blwebform\" style=\"portable\" hash=\"abcd\">\n" +
            "    Private Sub InitializeComponent()\n" +
            "        btn = New Button()\n" +
            "    End Sub\n" +
            "    ' </vgs:designer>\n" +
            "End Class\n"));

        Assert.That(result.Success, Is.True, Errors(result));
        Assert.That(Errors(result), Does.Not.Contain("RuntimeControlCreation"));
    }

    [Test]
    public void WebBuild_AllowsNewOfATrayComponent()
    {
        var result = Compile(Js(), ("Main.bas",
            "Using System\nUsing System.Windows.Forms\n\n" +
            "Module Program\n    Sub Main()\n        Dim t As New Timer()\n    End Sub\nEnd Module\n"));

        Assert.That(result.Success, Is.True, Errors(result));
        Assert.That(Errors(result), Does.Not.Contain("RuntimeControlCreation"));
    }

    [Test]
    public void DesktopBuild_AllowsNewOfAControl()
    {
        var result = Compile(Cs(), ("LoginForm.bas",
            "Using System.Windows.Forms\n\n" +
            "Public Class LoginForm\n" +
            "    Private btn As Button\n" +
            "    Public Sub Test()\n" +
            "        btn = New Button()\n" +
            "    End Sub\n" +
            "End Class\n"));

        Assert.That(result.Success, Is.True, Errors(result));
        Assert.That(Errors(result), Does.Not.Contain("RuntimeControlCreation"));
    }

    // ------------------------------------------------------- MixedCodeStyle

    [Test]
    public void WebBuild_RefusesDomMarkersWithUsingWinForms()
    {
        var result = Compile(Js(), ("LoginForm.bas",
            "Using System\nUsing System.Windows.Forms\n\n" +
            "Public Class LoginForm\n" +
            "    ' <vgs:designer region=\"controls\" form=\"LoginForm.blwebform\" hash=\"abcd\">\n" +
            "    Private btn As Button\n" +
            "    ' </vgs:designer>\n" +
            "End Class\n"));

        Assert.That(result.Success, Is.False);
        Assert.That(Errors(result), Does.Contain("MixedCodeStyle"));
    }

    [Test]
    public void WebBuild_RefusesDomMarkersWithInheritsForm()
    {
        var result = Compile(Js(), ("LoginForm.bas",
            "Using System\nUsing System.Windows.Forms\n\n" +
            "Public Class LoginForm\n" +
            "    Inherits Form\n" +
            "    ' <vgs:designer region=\"controls\" form=\"LoginForm.blwebform\" hash=\"abcd\">\n" +
            "    Private btn As Button\n" +
            "    ' </vgs:designer>\n" +
            "End Class\n"));

        Assert.That(result.Success, Is.False);
        Assert.That(Errors(result), Does.Contain("MixedCodeStyle"));
    }

    [Test]
    public void MixedCodeStyle_IsNotTriggeredByACommentOrString()
    {
        var result = Compile(Js(), ("LoginForm.bas",
            "Using System\n\n" +
            "Public Class LoginForm\n" +
            "    ' Using System.Windows.Forms\n" +
            "    ' Inherits Form\n" +
            "    Private note As String = \"Using System.Windows.Forms\"\n" +
            "    ' <vgs:designer region=\"controls\" form=\"LoginForm.blwebform\" hash=\"abcd\">\n" +
            "    Private shade As System.Drawing.Color\n" +
            "    ' </vgs:designer>\n" +
            "End Class\n"));

        Assert.That(result.Success, Is.True, Errors(result));
        Assert.That(Errors(result), Does.Not.Contain("MixedCodeStyle"));
    }

    [Test]
    public void PortableStyleMarkers_DoNotTriggerMixedCodeStyle()
    {
        var result = Compile(Js(), ("LoginForm.bas",
            "Using System\nUsing System.Windows.Forms\n\n" +
            "Public Class LoginForm\n" +
            "    Inherits Form\n" +
            "    ' <vgs:designer region=\"controls\" form=\"LoginForm.blwebform\" style=\"portable\" hash=\"abcd\">\n" +
            "    Private btn As Button\n" +
            "    ' </vgs:designer>\n" +
            "End Class\n"));

        Assert.That(result.Success, Is.True, Errors(result));
        Assert.That(Errors(result), Does.Not.Contain("MixedCodeStyle"));
    }

    // ------------------------------------------------------- ElementOnDesktop

    [Test]
    public void DesktopBuild_RefusesElementOnALibraryControl()
    {
        var result = Compile(Cs(), ("LoginForm.bas",
            "Using System.Windows.Forms\n\n" +
            "Public Class LoginForm\n" +
            "    Private btn As Button\n" +
            "    Public Sub Test()\n" +
            "        Dim e = btn.Element\n" +
            "    End Sub\n" +
            "End Class\n"));

        Assert.That(result.Success, Is.False);
        Assert.That(Errors(result), Does.Contain("ElementOnDesktop"));
        Assert.That(Errors(result), Does.Contain("web-only"));
    }

    [Test]
    public void DesktopBuild_RefusesElementOnMeInAFormSubclass()
    {
        var result = Compile(Cs(), ("LoginForm.bas",
            "Using System.Windows.Forms\n\n" +
            "Public Class LoginForm\n" +
            "    Inherits Form\n" +
            "    Public Sub Test()\n" +
            "        Dim e = Me.Element\n" +
            "    End Sub\n" +
            "End Class\n"));

        Assert.That(result.Success, Is.False);
        Assert.That(Errors(result), Does.Contain("ElementOnDesktop"));
    }

    [Test]
    public void DesktopBuild_AllowsElementInsideAWebBranch()
    {
        var result = Compile(Cs(), ("LoginForm.bas",
            "Using System.Windows.Forms\n\n" +
            "Public Class LoginForm\n" +
            "    Inherits Form\n" +
            "    Private btn As Button\n" +
            "    Public Sub Test()\n" +
            "#If WEB Then\n" +
            "        Dim e = btn.Element\n" +
            "#End If\n" +
            "    End Sub\n" +
            "End Class\n"));

        Assert.That(result.Success, Is.True, Errors(result));
        Assert.That(Errors(result), Does.Not.Contain("ElementOnDesktop"));
    }

    [Test]
    public void WebBuild_AllowsElementOnALibraryControl()
    {
        var result = Compile(Js(), ("LoginForm.bas", PortableFormHead +
            "    Public Sub Test()\n" +
            "        Dim e = btn.Element\n" +
            "    End Sub\n" +
            "End Class\n"));

        Assert.That(result.Success, Is.True, Errors(result));
        Assert.That(Errors(result), Does.Not.Contain("ElementOnDesktop"));
    }
}
