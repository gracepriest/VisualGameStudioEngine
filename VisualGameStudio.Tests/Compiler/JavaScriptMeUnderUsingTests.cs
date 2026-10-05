using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.IR.Optimization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// M7 (portable-controls Task 11) — with ANY <c>Using</c> line in the file, <c>Me.Init()</c> failed the JavaScript build:
/// "no lowering for 'Me.Init'". IsKnownNetStaticType answers true for every PascalCase name once the unit has a .NET
/// Using, and <c>Me</c> is PascalCase, so the call was routed as a static call on a type named Me. ⛔ Only the PROJECT
/// route sets CurrentUnit (ConfigureModuleSystem), so this fixture compiles through BasicCompiler — JsTestSupport would
/// pass with the defect present. The WinForms scaffold is exactly this shape (three Usings + Me.InitializeComponent()).
/// </summary>
[TestFixture]
[Category("Integration")]
public class JavaScriptMeUnderUsingTests
{
    private static string Run(string source)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bl-meusing-" + Path.GetRandomFileName())).FullName;
        try
        {
            var file = Path.Combine(dir, "Main.bas");
            File.WriteAllText(file, source);
            var r = new BasicCompiler(new CompilerOptions { TargetBackend = "javascript" }).CompileProjectFiles(new[] { file });
            Assert.That(r.HasErrors, Is.False, string.Join(" | ", r.AllErrors.Select(e => e.Message)));
            var pipeline = new OptimizationPipeline();
            pipeline.AddStandardPasses();
            pipeline.Run(r.CombinedIR!);
            return FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(new JavaScriptCodeGenerator().Generate(r.CombinedIR!)));
        }
        finally { Directory.Delete(dir, true); }
    }

    private const string Body =
        "Public Class Widget\n Public Sub Go()\n  Console.WriteLine(\"go\")\n End Sub\nEnd Class\n" +
        "Public Class Frm\n Private Button1 As Widget\n" +
        " Public Sub New()\n  Me.Init()\n  Console.WriteLine(Me.Twice(21))\n  Button1 = New Widget()\n  Button1.Go()\n  Use(Button1)\n End Sub\n" +
        " Private Sub Init()\n  Console.WriteLine(\"init\")\n End Sub\n" +
        " Public Function Twice(n As Integer) As Integer\n  Return n * 2\n End Function\n" +
        " Private Sub Use(Thing As Widget)\n  Thing.Go()\n End Sub\nEnd Class\n" +
        "Sub Main()\n Dim f As New Frm()\nEnd Sub\n";

    [TestCase("Using System\n")]
    [TestCase("Using System.Drawing\n")]
    [TestCase("Using System.Windows.Forms\n")]
    [TestCase("Using System\nUsing System.Drawing\nUsing System.Windows.Forms\n")]
    [TestCase("")]
    public void SelfCalls_FieldAndParameterReceivers_UnderAnyUsing(string usings) =>
        Assert.That(Run(usings + Body), Is.EqualTo("init\n42\ngo\ngo"));

    [Test]
    public void AnInheritedMethod_ThroughMe_UnderAUsing() =>
        Assert.That(Run("Using System\nPublic Class B0\n Public Sub Hello()\n  Console.WriteLine(\"hello\")\n End Sub\nEnd Class\n" +
                        "Public Class Frm\n Inherits B0\n Public Sub New()\n  Me.Hello()\n End Sub\nEnd Class\n" +
                        "Sub Main()\n Dim f As New Frm()\nEnd Sub\n"), Is.EqualTo("hello"));
}
