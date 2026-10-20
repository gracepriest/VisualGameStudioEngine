using System;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RecordingOutput = VisualGameStudio.Tests.Services.JavaScriptProjectBuildTests.RecordingOutput;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// O14 / spec ┬º4.11 ΓÇö Decimal on the JavaScript backend, EXACT, with .NET as the oracle (plan S10): each program runs on
/// the C# backend in-process (real System.Decimal, InvariantCulture) and on JavaScript plain and optimized, and the
/// three must agree. Measured before (M20): BL7007 "'Decimal' is not available on the JavaScript backend".
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class JavaScriptDecimalTests
{
    private CultureInfo _saved = CultureInfo.CurrentCulture;

    [SetUp] public void Invariant() { _saved = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; }
    [TearDown] public void Restore() => CultureInfo.CurrentCulture = _saved;

    /// <summary>Runs <paramref name="program"/> on C# (the oracle) and JavaScript (plain + optimized); returns the oracle's output.</summary>
    internal static string Both(string program)
    {
        var cs = FourBackends.Norm(FourBackends.RunEmittedCSharp(program));
        var js = FourBackends.Norm(JavaScriptExecutionTests.RunJs(program));
        var jsOpt = FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(program));
        Assert.Multiple(() =>
        {
            Assert.That(js, Is.EqualTo(cs), "JavaScript vs .NET");
            Assert.That(jsOpt, Is.EqualTo(cs), "JavaScript (optimized) vs .NET");
        });
        return cs;
    }

    /// <summary>A BasicLang Decimal literal for <paramref name="d"/>, scale AND SIGN kept ΓÇö `-0.0m` is not `&lt; 0`, so the
    /// sign is read from the bits (decimal.GetBits' flags word), never from a comparison.</summary>
    internal static string L(decimal d)
    {
        var negative = (decimal.GetBits(d)[3] & unchecked((int)0x80000000)) != 0;
        var magnitude = (negative ? -d : d).ToString(CultureInfo.InvariantCulture);
        return negative ? $"(-{magnitude}D)" : magnitude + "D";
    }

    /// <summary>VB-literal expectations (the exception rows E1ΓÇôE4) are asserted on BOTH backends.</summary>
    internal static void BothEqual(string program, string vbExpected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(vbExpected), "C# vs VB");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(vbExpected), "JavaScript vs VB");
            Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(program)), Is.EqualTo(vbExpected), "JavaScript (optimized) vs VB");
        });
    }

    /// <summary>Operands go through Id so no optimizer ΓÇö BasicLang's or Roslyn's ΓÇö folds them.</summary>
    internal const string Id = "Function Id(d As Decimal) As Decimal\n Return d\nEnd Function\n";

    // ------------------------------------------------------------ D1, D2, D11: literals, Const, text

    [Test]
    public void LiteralsKeepTheirScale() => Assert.That(Both(
        "Const Rate As Decimal = 0.075D\nSub Main()\n Console.WriteLine(1.10D)\n Console.WriteLine(5D)\n Console.WriteLine(0D)\n" +
        " Console.WriteLine(-2.500D)\n Console.WriteLine(79228162514264337593543950335D)\n Console.WriteLine(-79228162514264337593543950335D)\n" +
        " Console.WriteLine(0.0000000000000000000000000001D)\n Console.WriteLine(Rate)\nEnd Sub\n"),
        Is.EqualTo("1.10\n5\n0\n-2.500\n79228162514264337593543950335\n-79228162514264337593543950335\n0.0000000000000000000000000001\n0.075"));

    [Test]
    public void TheTextForms() => Assert.That(Both(Id +
        "Sub Main()\n Dim d As Decimal = Id(1.50D)\n Console.WriteLine(CStr(d))\n Console.WriteLine(d & \"!\")\n" +
        " Console.WriteLine($\"[{d}]\")\n Console.WriteLine(d.ToString())\n Console.WriteLine(String.Format(\"<{0}>\", d))\nEnd Sub\n"),
        Is.EqualTo("1.50\n1.50!\n[1.50]\n1.50\n<1.50>"));

    // ------------------------------------------------------------ the PROJECT route: UsesDecimal cannot miss

    /// <summary>
    /// Decimal appears ONLY in a sibling file's class field. A `UsesDecimal` scan that looked at one unit, or at locals
    /// only, would leave VgsDecimal out of App.js ΓÇö "VgsDecimal is not defined" at run time from a green build.
    /// Compiled through CompileProjectFiles, the real CLI and the IDE's BuildService.
    /// </summary>
    [Test]
    public async System.Threading.Tasks.Task DecimalOnlyInASiblingFilesField_OnEveryRoute()
    {
        var dir = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "bl-decproj-" + System.IO.Path.GetRandomFileName())).FullName;
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "Account.bas"),
                "Public Class Account\n Public Balance As Decimal = 2.50D\n Public Function Show() As String\n  Return CStr(Balance)\n End Function\nEnd Class\n");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "Main.bas"),
                "Module Program\n Sub Main()\n  Dim a As New Account()\n  Console.WriteLine(a.Show())\n End Sub\nEnd Module\n");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "Site.blproj"),
                "<Project>\n  <PropertyGroup>\n    <ProjectName>Site</ProjectName>\n    <TargetBackend>JavaScript</TargetBackend>\n" +
                "  </PropertyGroup>\n  <ItemGroup>\n    <Compile Include=\"Main.bas\" />\n    <Compile Include=\"Account.bas\" />\n  </ItemGroup>\n</Project>\n");

            var r = new BasicLang.Compiler.BasicCompiler(new BasicLang.Compiler.CompilerOptions { TargetBackend = "javascript" })
                .CompileProjectFiles(new[] { System.IO.Path.Combine(dir, "Main.bas"), System.IO.Path.Combine(dir, "Account.bas") });
            Assert.That(r.HasErrors, Is.False);
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(
                new BasicLang.Compiler.CodeGen.JavaScript.JavaScriptCodeGenerator().Generate(r.CombinedIR!))), Is.EqualTo("2.50"), "API");

            var (exit, stdout, stderr) = await CliTestHarness.RunCli(dir, "build", "Site.blproj");
            Assert.That(exit, Is.Zero, stdout + stderr);
            var js = System.IO.Directory.GetFiles(System.IO.Path.Combine(dir, "bin"), "Site.js", System.IO.SearchOption.AllDirectories).Single();
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(System.IO.File.ReadAllText(js))), Is.EqualTo("2.50"), "CLI");

            var project = await new VisualGameStudio.ProjectSystem.Serialization.ProjectSerializer().LoadAsync(System.IO.Path.Combine(dir, "Site.blproj"));
            var built = await new VisualGameStudio.ProjectSystem.Services.BuildService(new RecordingOutput()).BuildProjectAsync(project);
            Assert.That(built.Success, Is.True);
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(built.GeneratedCode)), Is.EqualTo("2.50"), "IDE");
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }

    // ------------------------------------------------------------ D22: members, parameters, returns, defaults

    [Test]
    public void FieldsPropertiesParametersReturnsAndDefaults() => Both(Id +
        "Public Class Account\n Public Balance As Decimal\n Private _limit As Decimal = 100.00D\n" +
        " Public Property Limit As Decimal\n  Get\n   Return _limit\n  End Get\n  Set(value As Decimal)\n   _limit = value\n  End Set\n End Property\nEnd Class\n" +
        "Function Pass(d As Decimal) As Decimal\n Return d\nEnd Function\n" +
        "Sub Main()\n Dim a As New Account()\n Console.WriteLine(a.Balance)\n Console.WriteLine(a.Limit)\n a.Limit = Id(2.5D)\n" +
        " Console.WriteLine(a.Limit)\n Console.WriteLine(Pass(Id(7.000D)))\n Dim local As Decimal\n Console.WriteLine(local)\nEnd Sub\n");
    /// <summary>MIT: the ported code ships inside every user's App.js, so the notice must be IN the emitted prelude.</summary>
    [Test]
    public void TheEmittedPrelude_CarriesTheDotnetRuntimeNotice()
    {
        var js = JsTestSupport.CompileOptimized("Sub Main()\n Dim d As Decimal = 1.5D\n Console.WriteLine(d)\nEnd Sub\n");
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("class VgsDecimal"));
            Assert.That(js, Does.Contain("Portions derived from dotnet/runtime (System.Decimal.DecCalc"));
            Assert.That(js, Does.Contain("MIT License"));
        });
    }
}
