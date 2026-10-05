using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.IR.Optimization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Review of f6df2469 / f1140d0c / 10bd3b68 (portable-controls Tasks 8–10): the Enum shapes the portable library needs
/// (Keys, DockStyle, AnchorStyles, …) and the handler shapes users write, each RUN on C#, JavaScript and C++. Probes in
/// scratchpad <c>rv10p\</c>.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class EnumAndHandlerReviewTests
{
    private static void RunsOnCsJsCpp(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(expected), "JavaScript");
        });
        // Outside Assert.Multiple: CompileRun IGNORES when there is no C++ compiler.
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(expected), "C++");
    }

    private static void RunsOnCsJs(string program, string expected) =>
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(expected), "JavaScript");
        });

    private const string Shade ="Enum Shade\n Light\n Dark\n Keyed = 65\nEnd Enum\n";

    /// <summary>VB and C# print an Enum value as its member NAME — <c>ToString()</c>, <c>Console.WriteLine</c> and
    /// <c>&amp;</c> alike. JavaScript printed the number; C++ did not compile (C2679 / C2676).</summary>
    [Test]
    public void AnEnumValue_PrintsAsItsName() => RunsOnCsJsCpp(Shade +
        "Sub Main()\n Dim k As Shade = Shade.Dark\n Console.WriteLine(k.ToString())\n Console.WriteLine(k)\n" +
        " Console.WriteLine(\"v=\" & k)\n Dim s As String = k.ToString()\n Console.WriteLine(s & \"!\")\nEnd Sub\n",
        "Dark\nDark\nv=Dark\nDark!");

    /// <summary>A FIELD initialized with an Enum member (refused "at line 0" before).</summary>
    [Test]
    public void AnEnumFieldInitializer_Compiles() => RunsOnCsJsCpp(Shade +
        "Class Holder\n Public K As Shade = Shade.Keyed\nEnd Class\n" +
        "Sub Main()\n Dim h As New Holder()\n Console.WriteLine(CInt(h.K))\nEnd Sub\n", "65");

    /// <summary>VB widens an Enum to an integral type implicitly.</summary>
    [Test]
    public void AnEnum_WidensToInteger() => RunsOnCsJsCpp(Shade +
        "Sub Main()\n Dim n As Integer = Shade.Keyed\n Console.WriteLine(n)\n Dim m As Double = Shade.Dark\n Console.WriteLine(m)\nEnd Sub\n",
        "65\n1");

    /// <summary>Flags: <c>Or</c>/<c>And</c> of two values of one Enum is that Enum (the library's
    /// <c>AnchorStyles.Top Or AnchorStyles.Left</c>), and two values of one Enum compare. (<c>Xor</c> is not a BasicLang
    /// binary operator at all — a separate language gap, recorded in the plan.)</summary>
    [Test]
    public void FlagsAndComparisons_OnOneEnum() => RunsOnCsJsCpp(
        "Enum Style\n None = 0\n Bold = 1\n Italic = 2\n Under = 4\nEnd Enum\n" +
        "Sub Main()\n Dim st As Style = Style.Bold Or Style.Italic\n Console.WriteLine(CInt(st))\n" +
        " If (st And Style.Italic) = Style.Italic Then Console.WriteLine(\"italic\")\n" +
        " If (st And Style.Under) = Style.None Then Console.WriteLine(\"not under\")\n" +
        " st = st And Style.Italic\n Console.WriteLine(CInt(st))\n" +
        " If Style.Bold < Style.Under Then Console.WriteLine(\"less\")\n" +
        " If st <> Style.None Then Console.WriteLine(\"some\")\nEnd Sub\n",
        "3\nitalic\nnot under\n2\nless\nsome");

    /// <summary>VB's Case takes any expression: a NON-constant Shared field as a Case value is a run-time compare
    /// (C# CS9135 before — <c>case Lim.Max:</c> needs a constant).</summary>
    [Test]
    public void ANonConstantCaseValue_Runs() => RunsOnCsJsCpp(
        "Class Lim\n Public Shared Max As Integer = 7\nEnd Class\n" +
        "Sub Main()\n Dim v As Integer = 7\n Select Case v\n  Case 1\n   Console.WriteLine(\"one\")\n  Case Lim.Max\n   Console.WriteLine(\"max\")\n" +
        "  Case Else\n   Console.WriteLine(\"else\")\n End Select\nEnd Sub\n", "max");

    /// <summary>VB's relaxed delegates: a handler with NO parameters, or with wider (Object) parameters, is accepted by
    /// VB and passed BasicLang — and C# refused it (CS0123). It must run. C# and JavaScript: C++ refuses <c>Object</c>
    /// by capability ("'Object' has no C++ mapping"), and C++ is not a forms target.</summary>
    [Test]
    public void RelaxedHandlers_Run() => RunsOnCsJs(
        "Public Class Btn\n Public Event Click(sender As Object, e As Integer)\n Public Sub Fire()\n  RaiseEvent Click(Me, 3)\n End Sub\nEnd Class\n" +
        "Public Class Form1\n Private b As Btn\n Public Sub New()\n  b = New Btn()\n" +
        "  AddHandler b.Click, AddressOf NoArgs\n  AddHandler b.Click, AddressOf Wide\n  b.Fire()\n End Sub\n" +
        " Private Sub NoArgs()\n  Console.WriteLine(\"noargs\")\n End Sub\n" +
        " Private Sub Wide(sender As Object, e As Object)\n  Console.WriteLine(\"wide \" & CStr(e))\n End Sub\nEnd Class\n" +
        "Sub Main()\n Dim f As New Form1()\nEnd Sub\n", "noargs\nwide 3");

    /// <summary>A handler that is a MODULE procedure (C# CS0103 before: it was spelled bare inside the class). C# and
    /// JavaScript: an event with parameters does not compile on C++ at all (pre-existing — `raise_X()` takes none).</summary>
    [Test]
    public void AModuleProcedure_AsAHandler_Runs() => RunsOnCsJs(
        "Public Class Btn\n Public Event Click(sender As Btn, e As Integer)\n Public Sub Fire()\n  RaiseEvent Click(Me, 3)\n End Sub\nEnd Class\n" +
        "Module Handlers\n Public Sub OnClick(sender As Btn, e As Integer)\n  Console.WriteLine(\"module \" & e)\n End Sub\nEnd Module\n" +
        "Public Class Form1\n Private b As Btn\n Public Sub New()\n  b = New Btn()\n  AddHandler b.Click, AddressOf OnClick\n  b.Fire()\n End Sub\nEnd Class\n" +
        "Sub Main()\n Dim f As New Form1()\nEnd Sub\n", "module 3");

    // ---- multi-file ----------------------------------------------------------------------------------------------

    private string _dir = "";

    [SetUp]
    public void SetUp() => _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bl-rv10-" + Path.GetRandomFileName())).FullName;

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    private string[] Write(params (string Name, string Text)[] files) =>
        files.Select(f => { var p = Path.Combine(_dir, f.Name); File.WriteAllText(p, f.Text); return p; }).ToArray();

    /// <summary>
    /// ⛔ An <c>Enum Shade</c> in one file and a <c>Module Shade</c> in another is a duplicate name (VB: two types of one
    /// name in a namespace). Refused in EVERY file order — after Task 9 one order built clean and JavaScript died with
    /// <c>TypeError: Shade.Pick is not a function</c>.
    /// </summary>
    [Test]
    public void AnEnumAndAModuleOfOneName_AreRefused_InEveryOrder()
    {
        var paths = Write(
            ("Shade.bas", Shade),
            ("Colors.bas", "Module Shade\n Public Function Pick() As Integer\n  Return 3\n End Function\nEnd Module\n"),
            ("Main.bas", "Module Program\n Sub Main()\n  Console.WriteLine(Shade.Pick())\n  Dim k As Shade = Shade.Dark\n End Sub\nEnd Module\n"));
        var orders = new[]
        {
            new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 }
        };
        Assert.Multiple(() =>
        {
            foreach (var order in orders)
            {
                var files = order.Select(i => paths[i]).ToArray();
                var r = new BasicCompiler(new CompilerOptions { TargetBackend = "javascript" }).CompileProjectFiles(files);
                Assert.That(r.AllErrors.Select(e => e.Message), Has.Some.Contains("Shade"),
                    "[" + string.Join(",", files.Select(Path.GetFileName)) + "] must be refused");
            }
        });
    }

    /// <summary>
    /// "Main" → "Program" is a rename, so it is checked against every OTHER container too: Main.bas with file-level
    /// procedures beside a <c>Module Program</c> elsewhere emitted two <c>static class Program</c>s (CS0101).
    /// </summary>
    [Test]
    public void MainBas_BesideAModuleNamedProgram_RunsOnCSharp()
    {
        var paths = Write(
            ("Main.bas", "Sub Main()\n Helper()\n Console.WriteLine(Twice(4))\nEnd Sub\nSub Helper()\n Console.WriteLine(\"helped\")\nEnd Sub\n"),
            ("Util.bas", "Module Program\n Public Function Twice(n As Integer) As Integer\n  Return n * 2\n End Function\nEnd Module\n"));
        foreach (var order in new[] { paths, paths.Reverse().ToArray() })
        {
            var r = new BasicCompiler(new CompilerOptions { TargetBackend = "csharp" }).CompileProjectFiles(order);
            Assert.That(r.HasErrors, Is.False, string.Join(" | ", r.AllErrors.Select(e => e.Message)));
            var pipeline = new OptimizationPipeline();
            pipeline.AddStandardPasses();
            pipeline.Run(r.CombinedIR!);
            var cs = new CSharpCodeGenerator().Generate(r.CombinedIR!);
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(cs)), Is.EqualTo("helped\n8"), cs);
        }
    }
}
