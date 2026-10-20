using System;
using System.Globalization;
using System.Linq;
using System.Text;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
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
    // ------------------------------------------------------------ D3, D20: arithmetic, overflow, division by zero

    /// <summary>
    /// ΓëÑ 200 cases (D3): every ordered pair of these 15 values = 225 pairs. The set covers scale alignment (1.10 vs
    /// 2.205), the 28-digit cut (0.0000000000000000000000000001, 0.3333ΓÇª3), 96-bit overflow (Max, Min), negative zero
    /// (built from bits, since the literal -0.0m is not &lt; 0 ΓÇö see L), zero divisors, and a mixed-scale large value.
    /// </summary>
    private static readonly decimal[] Values =
    {
        1.10m, 2.205m, 0.1m, 3m, -7.5m, 0.5m, 123456789.123456789m, 0.0000000000000000000000000001m,
        -0.001m, decimal.MaxValue, decimal.MinValue, 0.3333333333333333333333333333m,
        12345678901234567890.12345678m, 0m, new decimal(0, 0, 0, true, 1) /* -0.0 */,
    };

    private static readonly (decimal A, decimal B)[] Pairs =
        Values.SelectMany(a => Values.Select(b => (a, b))).ToArray();

    private static string Guarded(string expression) =>
        $" Try\n  Console.WriteLine({expression})\n Catch ex As OverflowException\n  Console.WriteLine(\"overflow\")\n" +
        " Catch ex As DivideByZeroException\n  Console.WriteLine(\"div0\")\n End Try\n";

    [Test]
    public void TheArithmeticTable()
    {
        var sb = new StringBuilder(Id).Append("Sub Main()\n Dim a As Decimal\n Dim b As Decimal\n");
        foreach (var (a, b) in Pairs)
        {
            sb.Append($" a = Id({L(a)})\n b = Id({L(b)})\n");
            foreach (var op in new[] { "+", "-", "*", "/", "Mod" }) sb.Append(Guarded($"a {op} b"));
            sb.Append(Guarded("-a"));
        }
        var oracle = Both(sb.Append("End Sub\n").ToString());
        Assert.That(oracle.Split('\n'), Has.Length.EqualTo(Pairs.Length * 6), "every row printed exactly one line");
    }

    [Test]
    public void KnownAnswers() => Assert.That(Both(Id +
        "Sub Main()\n Console.WriteLine(Id(1D) / Id(3D))\n Console.WriteLine(Id(2D) / Id(3D))\n Console.WriteLine(Id(0.1D) + Id(0.2D))\n" +
        " Console.WriteLine(Id(1.10D) * Id(2D))\nEnd Sub\n"),
        Is.EqualTo("0.3333333333333333333333333333\n0.6666666666666666666666666667\n0.3\n2.20"));

    // ------------------------------------------------------------ D5, D6, D19

    [Test]
    public void TheComparisonTable()
    {
        var sb = new StringBuilder(Id).Append("Sub Main()\n Dim a As Decimal\n Dim b As Decimal\n");
        foreach (var (a, b) in Pairs.Append((1.10m, 1.1m)).Append((-0.0m, 0m)))
        {
            sb.Append($" a = Id({L(a)})\n b = Id({L(b)})\n");
            foreach (var op in new[] { "=", "<>", "<", "<=", ">", ">=" }) sb.Append($" Console.WriteLine(a {op} b)\n");
        }
        Both(sb.Append("End Sub\n").ToString());
    }

    [Test]
    public void ScaleDoesNotMatterToEquality() => Assert.That(Both(Id +
        "Sub Main()\n Console.WriteLine(Id(1.10D) = Id(1.1D))\nEnd Sub\n"), Is.EqualTo("True"));

    [Test]
    public void SelectCase() => Both(Id +
        "Sub Show(d As Decimal)\n Select Case d\n  Case 1.1D\n   Console.WriteLine(\"one point one\")\n" +
        "  Case 2D To 3D\n   Console.WriteLine(\"two to three\")\n  Case Is > 10D\n   Console.WriteLine(\"big\")\n" +
        "  Case Else\n   Console.WriteLine(\"else\")\n End Select\nEnd Sub\n" +
        "Sub Main()\n Show(Id(1.10D))\n Show(Id(2.50D))\n Show(Id(3.00D))\n Show(Id(10.01D))\n Show(Id(-1D))\nEnd Sub\n");

    /// <summary>No Decimal anywhere: the VgsDecimal prelude is ABSENT, so a both-Object equality must keep
    /// `===` rather than call into a class the backend never emitted (a ReferenceError from a green build).</summary>
    [Test]
    public void TwoObjectsWithoutADecimal_DoNotNeedTheDecimalPrelude() => Assert.That(Both(
        "Function Box(n As Integer) As Object\n Return n\nEnd Function\n" +
        "Sub Main()\n Dim a As Object = Box(5)\n Dim b As Object = a\n Console.WriteLine(a = b)\nEnd Sub\n"),
        Is.EqualTo("True"));

    /// <summary>Oracle rows: Equals and unboxing a boxed DECIMAL. NOTE: the plan's `TypeOf o Is Decimal` row
    /// was DROPPED — the ANALYZER refuses it before any backend ("'TypeOf … Is Decimal' is not supported: the
    /// type must be a class or an interface"), the same refusal on every target, so no backend arm is reachable.</summary>
    [Test]
    public void Boxing() => Both(Id +
        "Sub Main()\n Dim o As Object = Id(1.10D)\n Dim p As Object = Id(1.1D)\n Console.WriteLine(o.Equals(p))\n" +
        " Dim back As Decimal = CType(o, Decimal)\n Console.WriteLine(back)\nEnd Sub\n");

    /// <summary>EXCEPTION ROWS E2/E3 (VB-literal, S10): VB compares boxed numbers by VALUE (spec D19, owner-approved)
    /// and converts a boxed Integer/Double to Decimal. The C# backend compares references and unboxes strictly
    /// until Task 20A; this test drives that fix on C# too.</summary>
    [Test]
    public void Boxing_VbRules() => BothEqual(Id +
        "Function Box(n As Integer) As Object\n Return n\nEnd Function\n" +
        "Public Class Thing\nEnd Class\n" +
        "Sub Main()\n Dim o As Object = Id(1.10D)\n Dim p As Object = Id(1.1D)\n Console.WriteLine(o = p)\n Console.WriteLine(o <> p)\n" +
        " Dim i As Object = Box(5)\n Console.WriteLine(CType(i, Decimal))\n Dim five As Object = Id(5D)\n Console.WriteLine(i = five)\n" +
        " Console.WriteLine(i = Id(5D))\n" +
        " Dim t As Object = New Thing()\n Try\n  Console.WriteLine(CType(t, Decimal))\n Catch ex As InvalidCastException\n  Console.WriteLine(\"invalid cast\")\n End Try\nEnd Sub\n",
        "True\nFalse\n5\nTrue\nTrue\ninvalid cast");

    // ------------------------------------------------------------ D7, D8: conversions

    private const string IdD = "Function IdD(x As Double) As Double\n Return x\nEnd Function\n";

    private static string Overflow(string expression) =>
        $" Try\n  Console.WriteLine({expression})\n Catch ex As OverflowException\n  Console.WriteLine(\"overflow\")\n End Try\n";

    /// <summary>Oracle rows: CInt (C# already lowers it through Convert — banker's), CDec, widening.</summary>
    [Test]
    public void Conversions() => Both(Id + IdD +
        "Sub Main()\n Console.WriteLine(CInt(Id(2.5D)))\n Console.WriteLine(CInt(Id(3.5D)))\n Console.WriteLine(CInt(Id(-2.5D)))\n" +
        Overflow("CInt(Id(2147483648D))") +
        " Console.WriteLine(CDec(IdD(0.1)))\n Console.WriteLine(CDec(IdD(1.0 / 3.0)))\n Console.WriteLine(CDec(\"12.345\"))\n" +
        " Dim n As Integer = 5\n Dim d As Decimal = n\n Console.WriteLine(d)\n Dim s As Short = 7\n d = s\n Console.WriteLine(d)\n" +
        " Dim y As Byte = 9\n d = y\n Console.WriteLine(d)\nEnd Sub\n");

    /// <summary>EXCEPTION ROWS E1/E4 (VB-literal, S10) — CType/CShort/CByte of a Decimal round half-to-even and throw
    /// OverflowException out of range, on BOTH backends (C# fixed in Task 20A). NOTE: the plan's implicit narrowing row
    /// (`Dim j As Integer = Id(3.5D)`) was DROPPED — the ANALYZER refuses implicit Decimal→Integer on every target
    /// (measured in Task 20A), so CType is the only reachable path. Expected output is the plan's minus that row's 4.</summary>
    [Test]
    public void Narrowing_VbRules() => BothEqual(Id +
        "Sub Main()\n Console.WriteLine(CType(Id(2.5D), Integer))\n Console.WriteLine(CType(Id(3.5D), Integer))\n" +
        " Console.WriteLine(CShort(Id(2.5D)))\n Console.WriteLine(CByte(Id(255.4D)))\n" +
        Overflow("CType(Id(2147483648D), Integer)") + Overflow("CShort(Id(40000D))") + Overflow("CByte(Id(256D))") + Overflow("CByte(Id(-1D))") +
        "End Sub\n",
        "2\n4\n2\n255\noverflow\noverflow\noverflow\noverflow");

    /// <summary>
    /// Decimal → Double (VarR8FromDec) and Double → Decimal (VarDecFromR8), GENERATED: 400 decimals and 400 doubles
    /// from a fixed seed (random scale 0–28, random 96-bit mantissas, plus the edges: ±Max, 1e-28, values whose double
    /// is inexact, doubles near 1e15/1e16 digit boundaries, subnormal-adjacent tiny values that .NET turns into 0,
    /// ±7.9e28 overflow). Decimal → Double is asserted as `CDbl(Id(d)) = IdD(<the C#-computed double, "R">)` printing
    /// True (no double FORMATTING in the comparison); Double → Decimal prints the Decimal (exact text).
    /// </summary>
    [Test]
    public void DoubleConversionTables_MatchDotNet()
    {
        var rng = new Random(20260929);
        var sb = new StringBuilder(Id + IdD).Append("Sub Main()\n");
        for (var i = 0; i < 400; i++)
        {
            var d = new decimal(rng.Next(), rng.Next(), rng.Next(), rng.Next(2) == 0, (byte)rng.Next(29));
            var expected = ((double)d).ToString("R", CultureInfo.InvariantCulture);
            if (!expected.Contains('E') && !expected.Contains('.')) expected += ".0";
            sb.Append($" Console.WriteLine(CDbl(Id({L(d)})) = IdD({expected}))\n");
        }
        foreach (var x in new[] { 0.1, 1.0 / 3.0, 123456789012345.6, 1e15 - 0.5, 9999999999999999.0, 1e-28, 1e-29, 7.9e28, -2.5e-10 })
            sb.Append(Overflow($"CDec(IdD({x.ToString("R", CultureInfo.InvariantCulture)}))"));
        for (var i = 0; i < 400; i++)
        {
            var x = (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-20, 28));
            sb.Append(Overflow($"CDec(IdD({x.ToString("R", CultureInfo.InvariantCulture)}))"));
        }
        var oracle = Both(sb.Append("End Sub\n").ToString());
        Assert.That(oracle.Split('\n').Take(400), Is.All.EqualTo("True"), "the C# oracle agrees with (double)decimal itself");
    }

    // ------------------------------------------------------------ D10: the mixing grid

    private static string Outcome(Func<string> run)
    {
        try { return "OK:" + FourBackends.Norm(run()); }
        catch (InvalidOperationException ex) { return "REFUSED:" + ex.Message.Split('\n').FirstOrDefault(l => l.StartsWith("Diagnostics:")); }
    }

    /// <summary>The C# leg of a grid cell, never asserting: analyzer refusal → REFUSED, csc refusal → CSFAIL (via
    /// FourBackends.TryCompileCSharp, Task 20A), else the run's output. ⚠ This does NOT use
    /// ReturnCoercionTests.EmitCSharpForTest: that helper ASSERTS on a refused source (the plan's exact
    /// `CSharpOutcome` would die inside the analyzer and fail the whole grid on its first Single/Double cell),
    /// so the same pipeline is run here with a non-asserting Diagnostics exception instead.</summary>
    private static string CSharpOutcome(string program)
    {
        string cs;
        try { cs = EmitCSharpForMixingGrid(program); }
        catch (InvalidOperationException ex) { return "REFUSED:" + ex.Message.Split('\n').FirstOrDefault(l => l.StartsWith("Diagnostics:")); }
        var errors = FourBackends.TryCompileCSharp(cs);
        return errors.Count > 0 ? "CSFAIL:" + errors[0].Id : "OK:" + FourBackends.Norm(FourBackends.RunEmittedCSharpText(cs));
    }

    /// <summary>EmitCSharpForTest's pipeline, without the assertions (see CSharpOutcome).</summary>
    private static string EmitCSharpForMixingGrid(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        if (parser.Errors.Count > 0)
            throw new InvalidOperationException(
                $"Diagnostics: {string.Join("; ", parser.Errors.Select(e => e.Message))}{Environment.NewLine}");
        var analyzer = new SemanticAnalyzer();
        if (!analyzer.Analyze(ast))
            throw new InvalidOperationException(
                $"Diagnostics: {string.Join("; ", analyzer.Errors.Select(e => e.Message))}{Environment.NewLine}");
        var module = new IRBuilder(analyzer).Build(ast, "MixingGrid");
        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(module);
        return new ImprovedCSharpCodeGenerator().Generate(module);
    }

    /// <summary>
    /// Every numeric type against Decimal, both orders, six operators: each cell either lowers and prints what .NET
    /// prints, or the ANALYZER refuses it on both targets with the same words. No cell may reach a JS TypeError
    /// (RunNodeScript asserts exit 0), and no cell may be accepted by the analyzer and rejected by csc (a CSFAIL is an
    /// analyzer defect: make the analyzer refuse it — the one rule for both targets — and record the cell).
    /// </summary>
    [Test]
    public void TheMixingGrid()
    {
        var findings = new StringBuilder();
        foreach (var t in new[] { "Byte", "Short", "Integer", "Single", "Double" })
        foreach (var op in new[] { "+", "-", "*", "/", "=", "<" })
        foreach (var reversed in new[] { false, true })
        {
            var expr = reversed ? $"y {op} x" : $"x {op} y";
            var program = Id + $"Sub Main()\n Dim x As {t} = 3\n Dim y As Decimal = Id(1.5D)\n Console.WriteLine({expr})\nEnd Sub\n";
            var cs = CSharpOutcome(program);
            var js = Outcome(() => JavaScriptExecutionTests.RunJs(program));
            if (cs.StartsWith("CSFAIL") || cs != js) findings.Append($"{t} {expr}: C#={cs} | JS={js}\n");
        }
        Assert.That(findings.ToString(), Is.Empty);
    }

    // ------------------------------------------------------------ D13, D15

    [Test]
    public void MathOnDecimal() => Both(Id +
        "Sub Main()\n Console.WriteLine(Math.Round(Id(2.5D)))\n Console.WriteLine(Math.Round(Id(3.5D)))\n Console.WriteLine(Math.Round(Id(-2.5D)))\n" +
        " Console.WriteLine(Math.Round(Id(1.2345D), 2))\n Console.WriteLine(Math.Round(Id(1.235D), 2))\n Console.WriteLine(Math.Round(Id(1.5D), 3))\n" +
        " Console.WriteLine(Math.Abs(Id(-1.50D)))\n Console.WriteLine(Math.Min(Id(1.1D), Id(1.10D)))\n Console.WriteLine(Math.Max(Id(1.1D), Id(1.10D)))\n" +
        " Console.WriteLine(Math.Floor(Id(-1.5D)))\n Console.WriteLine(Math.Ceiling(Id(-1.5D)))\n Console.WriteLine(Math.Truncate(Id(-1.7D)))\n" +
        " Console.WriteLine(Math.Floor(Id(1.50D)))\nEnd Sub\n");

    [Test]
    public void Parse() => Both(
        "Sub Try1(s As String)\n Try\n  Console.WriteLine(Decimal.Parse(s))\n Catch ex As FormatException\n  Console.WriteLine(\"format\")\n" +
        " Catch ex As OverflowException\n  Console.WriteLine(\"overflow\")\n End Try\nEnd Sub\n" +
        "Sub Main()\n Try1(\"1,234.50\")\n Try1(\" -3.14 \")\n Try1(\"12.345\")\n Try1(\"1.\")\n Try1(\".5\")\n Try1(\"5-\")\n" +
        " Try1(\"abc\")\n Try1(\"1e5\")\n Try1(\"\")\n Try1(\"0.12345678901234567890123456789\")\n" +
        " Try1(\"79228162514264337593543950336\")\n Try1(\"-79228162514264337593543950336\")\nEnd Sub\n");

    // ------------------------------------------------------------ D17, D22

    [Test]
    public void ListsArraysAndOptional() => Both(Id +
        "Function Fee(Optional rate As Decimal = 0.05D) As Decimal\n Return rate\nEnd Function\n" +
        "Sub Main()\n Dim list As New List(Of Decimal)\n list.Add(Id(1.10D))\n list.Add(Id(2.205D))\n Dim sum As Decimal = 0D\n" +
        " For Each x As Decimal In list\n  sum = sum + x\n Next\n Console.WriteLine(sum)\n Console.WriteLine(list(1))\n Console.WriteLine(list.Count)\n" +
        " Dim arr() As Decimal = {1.1D, 2.2D}\n Console.WriteLine(arr(0) + arr(1))\n Console.WriteLine(Fee())\n Console.WriteLine(Fee(Id(0.1D)))\nEnd Sub\n");

    // ------------------------------------------------------------ D9: Long stays refused

    [Test]
    public void DecimalToLong_IsTheExistingLongRefusal() =>
        Assert.That(() => JsTestSupport.Compile("Sub Main()\n Dim d As Decimal = 1D\n Dim l As Long = CLng(d)\nEnd Sub"),
            Throws.Exception.With.Message.Contains("BL7003"));
}
