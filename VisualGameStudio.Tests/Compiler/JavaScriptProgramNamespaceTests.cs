using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.JavaScript;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec §4.9, M8 (portable-controls Task 15) — <c>Namespace System.Windows.Forms</c> was a parse error; the nested
/// spelling parsed, but <c>Using System.Windows.Forms</c> and <c>System.Windows.Forms.Button</c> then resolved as .NET
/// because every dotted Using is marked .NET at parse time. On a JavaScript build there is no .NET: a namespace the
/// PROGRAM declares wins. C# is unchanged (the library is never included there).
/// </summary>
[TestFixture]
[Category("Integration")]
public class JavaScriptProgramNamespaceTests
{
    private static BasicLang.Compiler.CompilationResult Compile(string backend, params (string Name, string Text)[] files)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bl-ns-" + Path.GetRandomFileName())).FullName;
        try
        {
            var paths = files.Select(f => { var p = Path.Combine(dir, f.Name); File.WriteAllText(p, f.Text); return p; }).ToArray();
            return new BasicCompiler(new CompilerOptions { TargetBackend = backend }).CompileProjectFiles(paths);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static string RunProject(params (string Name, string Text)[] files)
    {
        var r = Compile("javascript", files);
        Assert.That(r.HasErrors, Is.False, string.Join(" | ", r.AllErrors.Select(e => e.Message)));
        return FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(new JavaScriptCodeGenerator().Generate(r.CombinedIR!)));
    }

    // Names deliberately differ from the shipped library's classes: Task 28 makes a program
    // class that shadows Button/Control a named collision. This fixture still proves the
    // dotted-namespace resolution it was written for.
    private const string Library =
        "Namespace System.Windows.Forms\n" +
        "Public Class ControlX\n Public Overridable Function Describe() As String\n  Return \"Control\"\n End Function\nEnd Class\n" +
        "Public Class ButtonX\n Inherits ControlX\n Public Overrides Function Describe() As String\n  Return \"Button\"\n End Function\nEnd Class\n" +
        "End Namespace\n";

    private const string Form =
        "Using System\nUsing System.Windows.Forms\n" +
        "Public Class Form1\n Public Sub Go()\n  Dim b As ButtonX = New ButtonX()\n  Dim c As ControlX = b\n" +
        "  Dim q As System.Windows.Forms.ButtonX = b\n  PrintLine(c.Describe() & \"/\" & q.Describe())\n End Sub\nEnd Class\n" +
        "Module Program\n Sub Main()\n  Dim f As New Form1()\n  f.Go()\n End Sub\nEnd Module\n";

    [Test]
    public void ADottedNamespace_UsedThroughAUsing_AndQualified() =>
        Assert.That(RunProject(("Lib.bas", Library), ("Form1.bas", Form)), Is.EqualTo("Button/Button"));

    [Test]
    public void TheNestedSpelling_Works_Too() =>
        Assert.That(RunProject(
            ("Lib.bas", "Namespace System\nNamespace Windows\nNamespace Forms\n" +
                        Library.Replace("Namespace System.Windows.Forms\n", "").Replace("End Namespace\n", "") +
                        "End Namespace\nEnd Namespace\nEnd Namespace\n"),
            ("Form1.bas", Form)), Is.EqualTo("Button/Button"));

    [Test]
    public void ADottedNamespace_Parses() =>
        Assert.That(() => JsTestSupport.BuildModule("Namespace Acme.Widgets\nPublic Class W\nEnd Class\nEnd Namespace\nSub Main()\nEnd Sub"),
            Throws.Nothing);

    /// <summary>⛔ Guard (plan review): the library declares <c>Namespace System</c> (EventArgs) and System.Windows.Forms. A
    /// program that ALSO says <c>Using System</c> must keep Console, Math and a qualified System.Math working.</summary>
    [Test]
    public void TheStandardSurface_StillWorks_UnderUsingSystem_WhenTheProgramDeclaresSystemNamespaces() =>
        Assert.That(RunProject(
            ("Lib.bas", "Namespace System\nPublic Class EventArgs2\nEnd Class\nEnd Namespace\n" + Library),
            ("Main.bas", "Using System\nUsing System.Windows.Forms\nModule Program\n Sub Main()\n  Console.WriteLine(Math.Max(2, 3))\n" +
                         "  Console.WriteLine(System.Math.Abs(-4))\n  Dim b As New ButtonX()\n  Console.WriteLine(b.Describe())\n End Sub\nEnd Module\n")),
            Is.EqualTo("3\n4\nButton"));

    /// <summary>
    /// C# is UNCHANGED by the JavaScript-only rule: a program namespace's type reached qualified emits exactly the C# it
    /// emitted before Task 15 (pinned text), and still builds and runs.
    /// </summary>
    [Test]
    public void ACSharpBuild_QualifiedProgramType_IsUnchanged()
    {
        var r = Compile("csharp",
            ("Lib.bas", "Namespace Acme.Widgets\nPublic Class W\n Public Function N() As Integer\n  Return 5\n End Function\nEnd Class\nEnd Namespace\n"),
            ("Main.bas", "Module Program\n Sub Main()\n  Dim w As New Acme.Widgets.W()\n  Console.WriteLine(w.N())\n End Sub\nEnd Module\n"));
        Assert.That(r.HasErrors, Is.False, string.Join(" | ", r.AllErrors.Select(e => e.Message)));
        var cs = new CSharpCodeGenerator().Generate(r.CombinedIR!);
        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Contain("namespace Acme.Widgets"), cs);
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(cs)), Is.EqualTo("5"), cs);
        });
    }
}
