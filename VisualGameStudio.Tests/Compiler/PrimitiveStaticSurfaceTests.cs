using System;
using System.Linq;
using NUnit.Framework;
using BasicLang;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The Shared members of the type keywords — <c>String.Format</c>, <c>Integer.Parse</c>,
/// <c>Integer.MaxValue</c>, <c>Double.IsNaN</c>, <c>Char.IsDigit</c>, … (<see cref="PrimitiveStaticSurface"/>).
/// MEASURED on master f8ad07c:
/// <list type="bullet">
/// <item>None of them PARSED: "Unexpected token in expression: 'String'" (every type keyword lexes
/// as a keyword, and a keyword was no expression). <c>Math.Max</c>, an identifier, worked.</item>
/// <item>Once parsed, <c>String.Empty</c>, <c>Integer.Parse</c>, <c>Integer.MaxValue</c>, <c>Double.Parse</c>,
/// <c>Char.IsDigit</c> all typed Object — <c>Dim n As Integer = Integer.Parse(s)</c> was a conversion
/// error — and <c>Integer.MaxValue</c> reached C# verbatim (CS0103).</item>
/// <item>C++ emitted <c>StringFormat(...)</c>, a name that exists nowhere (a g++ error after "Compilation
/// successful"); JavaScript refused String.Format and printed <c>String.Empty &amp; "x"</c> as "undefinedx".</item>
/// </list>
/// C# is the reference: the expected text below is .NET 8's own output for these programs, including
/// its half-to-EVEN rounding of exact ties (0.125:F2 is "0.12") and the sign of a value that rounds to
/// zero (-0.04:F1 is "-0.0").
/// </summary>
public class PrimitiveStaticSurfaceTests
{
    /// <summary>Every row except Char's (JavaScript refuses the Char type, BL7004) and Long's (BL7003).</summary>
    internal const string Program = @"
Sub P(fmt As String, v As Double)
    Console.WriteLine(fmt & "" -> ["" & String.Format(fmt, v) & ""]"")
End Sub
Sub PI(fmt As String, v As Integer)
    Console.WriteLine(fmt & "" i-> ["" & String.Format(fmt, v) & ""]"")
End Sub
Sub TryParseInt(s As String)
    Try
        Console.WriteLine(""int("" & s & "")="" & Integer.Parse(s))
    Catch ex As FormatException
        Console.WriteLine(""int("" & s & "") FormatException: "" & ex.Message)
    Catch ex As OverflowException
        Console.WriteLine(""int("" & s & "") OverflowException: "" & ex.Message)
    End Try
End Sub
Sub TryParseDbl(s As String)
    Try
        Console.WriteLine(""dbl("" & s & "")="" & Double.Parse(s))
    Catch ex As FormatException
        Console.WriteLine(""dbl("" & s & "") FormatException"")
    End Try
End Sub
Sub TryFmt(fmt As String)
    Try
        Console.WriteLine(String.Format(fmt, 1, 2))
    Catch ex As FormatException
        Console.WriteLine(""fmt("" & fmt & "") FormatException"")
    End Try
End Sub
Sub Main()
    Dim s As String = String.Format(""{0}-{1}|{0,5}|{1,-4}|{2:F2}|{3:N0}|{4:D5}|{4:X}|{{x}}"", 1, ""a"", 3.14159, 1234567, 42)
    Console.WriteLine(s)
    Console.WriteLine(String.Join("","", ""a"", ""b"", ""c""))
    Console.WriteLine(String.Concat(""x"", 1, True))
    Console.WriteLine(String.IsNullOrEmpty(""""))
    Console.WriteLine(String.IsNullOrWhiteSpace(""  ""))
    Console.WriteLine(String.Empty & ""|"")
    Dim n As Integer = Integer.Parse("" -42 "") + 1
    Console.WriteLine(n)
    Console.WriteLine(Integer.MaxValue)
    Console.WriteLine(Integer.MinValue)
    Dim d As Double = Double.Parse(""2.5"") * 2
    Console.WriteLine(d)
    Console.WriteLine(Double.IsNaN(Double.NaN))
    Console.WriteLine(Double.IsInfinity(Double.PositiveInfinity))
    Console.WriteLine(Boolean.Parse(""true""))
    Console.WriteLine(Short.MaxValue)
    Console.WriteLine(Byte.MaxValue)
    P(""{0:F2}"", 0.125)
    P(""{0:E1}"", 1.25)
    P(""{0:E1}"", 1.35)
    P(""{0:E0}"", -2.5)
    P(""{0:N1}"", 1234.25)
    P(""{0:F1}"", 0.05)
    P(""{0:E2}"", -0.0)
    P(""{0:F2}"", 1E-10)
    P(""{0:N3}"", 999999.9995)
    P(""{0:F2}"", 0.375)
    P(""{0:F2}"", 2.675)
    P(""{0:F1}"", -0.04)
    P(""{0:F0}"", 2.5)
    P(""{0:F0}"", -2.5)
    P(""{0:N2}"", 1234567.891)
    P(""{0:N0}"", -999.5)
    P(""{0:F3}"", 1E+20)
    P(""{0:E2}"", 12345.678)
    P(""{0:e3}"", -0.00012345)
    P(""{0:E}"", 0)
    P(""{0:F2}"", -0.0)
    P(""{0}"", 1E+21)
    P(""{0,10:F2}"", 3.14159)
    P(""{0,-10:F2}|"", 3.14159)
    PI(""{0:X}"", -1)
    PI(""{0:x8}"", 255)
    PI(""{0:D3}"", -7)
    PI(""{0:N0}"", -1234567)
    PI(""{0:F1}"", 42)
    PI(""{0:E1}"", 1234)
    PI(""{0,6}"", 42)
    TryParseInt(""  +17  "")
    TryParseInt(""-2147483648"")
    TryParseInt(""2147483648"")
    TryParseInt(""12a"")
    TryParseInt("""")
    TryParseInt(""1,000"")
    TryParseDbl(""1,234.5"")
    TryParseDbl("" -1.5e3 "")
    TryParseDbl("".5"")
    TryParseDbl(""NaN"")
    TryParseDbl(""-Infinity"")
    TryParseDbl(""1e400"")
    TryParseDbl(""abc"")
    TryParseDbl(""5."")
    TryFmt(""{0}{1}"")
    TryFmt(""{2}"")
    TryFmt(""{0"")
    TryFmt(""}"")
    TryFmt(""{{}}{0:D2}"")
    Console.WriteLine(Boolean.Parse(""  FALSE ""))
    Console.WriteLine(Short.MinValue & "" "" & UInteger.MaxValue & "" "" & Byte.MinValue)
    Console.WriteLine(String.Format(""{0} {1} {2}"", Double.MaxValue, Double.Epsilon, Double.NegativeInfinity))
    Console.WriteLine(String.Format(""{0}|{1}|{2}"", True, ""x"", 2.5F))
End Sub
";

    internal const string Expected = "1-a|    1|a   |3.14|1,234,567|00042|2A|{x}\na,b,c\nx1True\nTrue\nTrue\n|\n-41\n2147483647\n-2147483648\n5\nTrue\nTrue\nTrue\n32767\n255\n{0:F2} -> [0.12]\n{0:E1} -> [1.2E+000]\n{0:E1} -> [1.4E+000]\n{0:E0} -> [-2E+000]\n{0:N1} -> [1,234.2]\n{0:F1} -> [0.1]\n{0:E2} -> [-0.00E+000]\n{0:F2} -> [0.00]\n{0:N3} -> [1,000,000.000]\n{0:F2} -> [0.38]\n{0:F2} -> [2.67]\n{0:F1} -> [-0.0]\n{0:F0} -> [2]\n{0:F0} -> [-2]\n{0:N2} -> [1,234,567.89]\n{0:N0} -> [-1,000]\n{0:F3} -> [100000000000000000000.000]\n{0:E2} -> [1.23E+004]\n{0:e3} -> [-1.234e-004]\n{0:E} -> [0.000000E+000]\n{0:F2} -> [-0.00]\n{0} -> [1E+21]\n{0,10:F2} -> [      3.14]\n{0,-10:F2}| -> [3.14      |]\n{0:X} i-> [FFFFFFFF]\n{0:x8} i-> [000000ff]\n{0:D3} i-> [-007]\n{0:N0} i-> [-1,234,567]\n{0:F1} i-> [42.0]\n{0:E1} i-> [1.2E+003]\n{0,6} i-> [    42]\nint(  +17  )=17\nint(-2147483648)=-2147483648\nint(2147483648) OverflowException: Value was either too large or too small for an Int32.\nint(12a) FormatException: The input string '12a' was not in a correct format.\nint() FormatException: The input string '' was not in a correct format.\nint(1,000) FormatException: The input string '1,000' was not in a correct format.\ndbl(1,234.5)=1234.5\ndbl( -1.5e3 )=-1500\ndbl(.5)=0.5\ndbl(NaN)=NaN\ndbl(-Infinity)=-Infinity\ndbl(1e400)=Infinity\ndbl(abc) FormatException\ndbl(5.)=5\n12\nfmt({2}) FormatException\nfmt({0) FormatException\nfmt(}) FormatException\n{}01\nFalse\n-32768 4294967295 0\n1.7976931348623157E+308 5E-324 -Infinity\nTrue|x|2.5";

    /// <summary>The Char and Long rows, and Join over an array — C# and C++ only.</summary>
    internal const string NativeProgram = @"
Sub Main()
    Console.WriteLine(Char.IsLetter(""q""c) & "" "" & Char.IsWhiteSpace("" ""c) & "" "" & Char.ToUpper(""m""c) & "" "" & Char.IsDigit(""x""c))
    Console.WriteLine(Char.IsLetterOrDigit(""_""c) & "" "" & Char.IsUpper(""Q""c) & "" "" & Char.IsLower(""Q""c) & "" "" & Char.ToLower(""Q""c))
    Console.WriteLine(Long.MaxValue & "" "" & Long.MinValue & "" "" & ULong.MaxValue)
    Dim big As Long = Long.Parse(""-9223372036854775808"")
    Console.WriteLine(big)
    Console.WriteLine(String.Format(""{0:X}|{1:D3}|{2}|{3}"", Long.MinValue, ""7""c, ""c""c, 3000000000L))
    Try
        Console.WriteLine(Long.Parse(""9223372036854775808""))
    Catch ex As OverflowException
        Console.WriteLine(""long overflow: "" & ex.Message)
    End Try
    Try
        Console.WriteLine(ULong.Parse(""-1""))
    Catch ex As OverflowException
        Console.WriteLine(""ulong overflow: "" & ex.Message)
    End Try
    Console.WriteLine(String.Join(""-"", ""a"", 1, True, 2.5))
    Dim parts() As String = {""x"", ""y"", ""z""}
    Console.WriteLine(String.Join(""/"", parts))
End Sub
";

    internal const string NativeExpected = "True True M False\nFalse True False q\n9223372036854775807 -9223372036854775808 18446744073709551615\n-9223372036854775808\n8000000000000000|7|c|3000000000\nlong overflow: Value was either too large or too small for an Int64.\nulong overflow: Value was either too large or too small for a UInt64.\na-1-True-2.5\nx/y/z";

    internal const string GuardProgram = @"
Function Pick(s As String) As String
    Select Case s.Length
        Case Is > 0 When Integer.Parse(s) > Short.MaxValue
            Return String.Format(""big {0:N0}"", Integer.Parse(s))
        Case Is > 0 When String.IsNullOrWhiteSpace(s) = False
            Return ""small""
        Case Else
            Return String.Empty & ""empty""
    End Select
End Function
Sub Main()
    Console.WriteLine(Pick(""40000""))
    Console.WriteLine(Pick(""7""))
    Console.WriteLine(Pick(""""))
End Sub
";

    internal const string GuardExpected = "big 40,000\nsmall\nempty";

    private static string[] ParseErrors(string body)
    {
        var parser = new Parser(new Lexer("Sub Main()\n" + body + "\nEnd Sub\n").Tokenize());
        parser.Parse();
        return parser.Errors.Select(e => e.Message).ToArray();
    }

    [TestCase("String.Format(\"{0}\", 1)")]
    [TestCase("Integer.Parse(\"1\")")]
    [TestCase("Integer.MaxValue")]
    [TestCase("Long.MinValue")]
    [TestCase("Double.IsNaN(0.5)")]
    [TestCase("Single.Epsilon")]
    [TestCase("Char.IsDigit(\"1\"c)")]
    [TestCase("Boolean.Parse(\"True\")")]
    [TestCase("Short.MaxValue")]
    [TestCase("Byte.MaxValue")]
    [TestCase("UShort.MaxValue")]
    [TestCase("UInteger.MaxValue")]
    [TestCase("ULong.MaxValue")]
    public void TypeKeyword_BeforeADot_ParsesAsTheType(string expr) =>
        Assert.That(ParseErrors($"    Console.WriteLine({expr})"), Is.Empty);

    /// <summary>Only before a dot: a bare keyword is still no expression, and a declaration is unchanged.</summary>
    [Test]
    public void BareTypeKeyword_IsStillNotAnExpression()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ParseErrors("    Console.WriteLine(String)"), Has.Some.Contains("Unexpected token in expression"));
            Assert.That(ParseErrors("    Dim s As String = \"a\"\n    Dim n As Integer = 1"), Is.Empty);
        });
    }

    /// <summary>Every row types as the table says — the analyzer consults the same table the backends implement.</summary>
    [Test]
    public void EveryRow_TypesAsTheTableSays()
    {
        foreach (var row in PrimitiveStaticSurface.Rows)
        {
            var access = row.IsProperty ? $"{row.TypeName}.{row.MemberName}" : $"{row.TypeName}.{row.MemberName}({SampleArgs(row)})";
            var source = $"Sub Main()\n    Dim r As {row.ReturnTypeName} = {access}\nEnd Sub\n";
            var ast = new Parser(new Lexer(source).Tokenize()).Parse();
            var analyzer = new SemanticAnalyzer();
            analyzer.Analyze(ast);
            Assert.That(analyzer.Errors.Select(e => e.Message), Is.Empty, source);
        }
    }

    private static string SampleArgs(PrimitiveStaticSurface.Row row) => (row.TypeName, row.MemberName) switch
    {
        ("String", "Format") => "\"{0}\", 1",
        ("String", "Join") => "\",\", \"a\", \"b\"",
        ("String", "Concat") => "\"a\", 1",
        ("String", _) => "\"a\"",
        ("Char", _) => "\"a\"c",
        (_, "Parse") => "\"1\"",
        _ => "1.5",
    };

    [Test]
    public void CSharp_SpellsTheReceiverAsCSharpsKeyword_AndCompiles()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(Program);
        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Contain("int.Parse("), cs);
            Assert.That(cs, Does.Contain("int.MaxValue"), cs);
            Assert.That(cs, Does.Contain("string.Format("), cs);
            Assert.That(cs, Does.Not.Contain("Integer."), cs);
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(Program), Is.Empty);
        });
    }

    /// <summary>Every row has a C++ runtime function — the table and the runtime cannot drift.</summary>
    [Test]
    public void EveryRow_HasACppRuntimeFunction()
    {
        foreach (var row in PrimitiveStaticSurface.Rows)
        {
            var name = $"{row.TypeName}_{row.MemberName}";
            Assert.That(CppPrimitiveStaticsRuntime.Source, Does.Contain($" {name}(").Or.Contain($"BL##_{row.MemberName}("),
                $"no C++ runtime function for {row.TypeName}.{row.MemberName}");
        }
    }

    [TestCase("String.Compare(\"a\", \"b\")", "'String.Compare' is not implemented on the C++ backend")]
    [TestCase("Integer.Size", "'Integer.Size' is not implemented on the C++ backend")]
    [TestCase("String.IsNullOrEmpty(\"a\", \"b\")", "does not take 2 argument(s)")]
    public void Cpp_AMemberOutsideTheTable_IsACleanDiagnostic(string expr, string message) =>
        Assert.That(() => BclE2E.CompileToCppOptimized($"Sub Main()\n    Console.WriteLine({expr})\nEnd Sub\n"),
            Throws.Exception.With.Message.Contains(message));

    [TestCase("String.Compare(\"a\", \"b\")", "no lowering for 'String.Compare'")]
    [TestCase("Integer.Size", "no lowering for 'Integer.Size'")]
    public void JavaScript_AMemberOutsideTheTable_IsRefused(string expr, string message) =>
        Assert.That(() => JsTestSupport.CompileAggressive($"Sub Main()\n    Console.WriteLine({expr})\nEnd Sub\n"),
            Throws.Exception.With.Message.Contains(message));

    /// <summary>The prelude is emitted only for a program that uses a row.</summary>
    [Test]
    public void JavaScript_PreludeIsAbsent_WithoutARow()
    {
        var js = JsTestSupport.CompileAggressive("Sub Main()\n    Console.WriteLine(\"a\")\nEnd Sub\n");
        Assert.That(js, Does.Not.Contain("__blFormat"), js);
    }
}

/// <summary>The programs on each backend, against .NET's own output.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class PrimitiveStaticSurfaceRunTests
{
    [Test]
    public void CSharp_Runs() =>
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(PrimitiveStaticSurfaceTests.Program)),
            Is.EqualTo(PrimitiveStaticSurfaceTests.Expected));

    [Test]
    public void Cpp_Runs() =>
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(PrimitiveStaticSurfaceTests.Program))),
            Is.EqualTo(PrimitiveStaticSurfaceTests.Expected));

    [Test]
    public void Cpp_Aggressive_Runs() =>
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(PrimitiveStaticSurfaceTests.Program))),
            Is.EqualTo(PrimitiveStaticSurfaceTests.Expected));

    [Test]
    public void JavaScript_Runs() =>
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.Compile(PrimitiveStaticSurfaceTests.Program))),
            Is.EqualTo(PrimitiveStaticSurfaceTests.Expected));

    [Test]
    public void JavaScript_Aggressive_Runs() =>
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileAggressive(PrimitiveStaticSurfaceTests.Program))),
            Is.EqualTo(PrimitiveStaticSurfaceTests.Expected));

    [Test]
    public void Native_CSharpAndCpp_Run()
    {
        // Not Assert.Multiple: CompileRun's no-compiler Assert.Ignore would FAIL inside one.
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(PrimitiveStaticSurfaceTests.NativeProgram)),
            Is.EqualTo(PrimitiveStaticSurfaceTests.NativeExpected));
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(PrimitiveStaticSurfaceTests.NativeProgram))),
            Is.EqualTo(PrimitiveStaticSurfaceTests.NativeExpected));
    }

    /// <summary>A row inside a When guard (emission suppressed, in no block) — JavaScript and C#.</summary>
    [Test]
    public void Guard_JavaScriptAndCSharp_Run()
    {
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(PrimitiveStaticSurfaceTests.GuardProgram)),
            Is.EqualTo(PrimitiveStaticSurfaceTests.GuardExpected));
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileAggressive(PrimitiveStaticSurfaceTests.GuardProgram))),
            Is.EqualTo(PrimitiveStaticSurfaceTests.GuardExpected));
    }
}
