using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>Short</c> and <c>SByte</c> arithmetic, and their <c>MinValue \ -1</c>, on every backend.
///
/// <para><b>MEASURED on master</b> (<c>7a62bdea</c>) for <c>Dim q As Short = s \ m1</c> with
/// s = -32768, m1 = -1, where .NET throws OverflowException ("Arithmetic operation resulted in an
/// overflow."):</para>
/// <code>
///   C#          did not BUILD: C# promotes short/sbyte operands to int, and the int result was
///               assigned to the Short temp — CS0266 "Cannot implicitly convert type 'int' to
///               'short'". Not only `\`: every Short/SByte + - * \ Mod assigned back failed.
///   C++         printed -32768: the promoted quotient 32768 fits int (no trap) and wrapped back.
///   JavaScript  printed 32768, a value no Short can hold.
/// </code>
/// <para>A narrow <c>Mod</c> by -1 is 0 everywhere, as in .NET. Fix: C# casts a narrow result back
/// (checked for <c>\</c>, unchecked otherwise — the width Integer arithmetic already wraps at);
/// C++ lowers a narrow <c>\</c> to <c>BasicLang::IntDivNarrow&lt;N&gt;</c>, JavaScript to
/// <c>__blNarrowDiv</c>, both of which range-check the promoted quotient.</para>
/// </summary>
[TestFixture]
public class NarrowIntegerDivisionTests
{
    internal const string Program = @"
Function MinShort() As Short
    Return -32768
End Function

Function MinSByte() As SByte
    Return -128
End Function

Function ShortOf(v As Short) As Short
    Return v
End Function

Sub Main()
    Dim s As Short = MinShort()
    Dim m1 As Short = -1
    Try
        Dim q As Short = s \ m1
        Console.WriteLine(""short div "" & q)
    Catch ex As OverflowException
        Console.WriteLine(""short div overflow: "" & ex.Message)
    End Try
    Dim r As Short = s Mod m1
    Console.WriteLine(""short mod "" & r)

    Dim b As SByte = MinSByte()
    Dim n1 As SByte = -1
    Try
        Dim q2 As SByte = b \ n1
        Console.WriteLine(""sbyte div "" & q2)
    Catch ex As OverflowException
        Console.WriteLine(""sbyte div overflow"")
    End Try
    Dim r2 As SByte = b Mod n1
    Console.WriteLine(""sbyte mod "" & r2)

    Dim a As Short = ShortOf(-7)
    Dim two As Short = ShortOf(2)
    Dim hundred As Short = ShortOf(100)
    Console.WriteLine(a \ two)
    Console.WriteLine(a Mod two)
    Console.WriteLine(hundred \ m1)
    Console.WriteLine(s \ two)
    Dim sum As Short = a + two
    Dim product As Short = a * two
    Dim diff As Short = a - two
    Console.WriteLine(sum & "" "" & product & "" "" & diff)
    Dim x As SByte = 5
    Dim y As SByte = x + x
    Console.WriteLine(y)
    Try
        Dim z As Short = 0
        Console.WriteLine(a \ z)
    Catch ex As DivideByZeroException
        Console.WriteLine(""short div by zero"")
    End Try
End Sub
";

    internal const string Expected =
        "short div overflow: Arithmetic operation resulted in an overflow.\n" +
        "short mod 0\n" +
        "sbyte div overflow\n" +
        "sbyte mod 0\n" +
        "-3\n-1\n-100\n-16384\n" +
        "-5 -14 -9\n" +
        "10\n" +
        "short div by zero";

    [Test]
    public void CSharp_NarrowArithmetic_Builds()
    {
        var errors = ReturnCoercionTests.CompileEmittedCSharpForTest(Program);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
    }

    [Test]
    public void CSharp_NarrowIntegerDivision_IsCheckedAndTheRestIsNot()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(Program);
        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Contain("checked((short)("), "a Short \\ must throw when its quotient does not fit");
            Assert.That(cs, Does.Contain("checked((sbyte)("));
            Assert.That(cs, Does.Contain("unchecked((short)("), "Short + - * wrap like Integer does on this backend");
        });
    }

    [Test]
    public void Cpp_NarrowIntegerDivision_GoesThroughTheRangeCheckedHelper()
    {
        var cpp = CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(Program));
        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("BasicLang::IntDivNarrow<int16_t>("));
            Assert.That(cpp, Does.Contain("BasicLang::IntDivNarrow<int8_t>("));
        });
    }

    [Test]
    public void Cpp_IntegerDivision_KeepsThePlainHelper()
        => Assert.That(CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(
                "Function Seven() As Integer\n    Return 7\nEnd Function\n" +
                "Sub Main()\n    Dim a As Integer = Seven()\n    Console.WriteLine(a \\ 2)\nEnd Sub\n")),
            Does.Contain("BasicLang::IntDiv(").And.Not.Contain("IntDivNarrow<"));

    [Test]
    public void Js_NarrowIntegerDivision_GoesThroughTheRangeCheckedHelper()
    {
        var js = JsTestSupport.CompileOptimized(Program);
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("function __blNarrowDiv(a, b, max)"));
            Assert.That(js, Does.Match(@"__blNarrowDiv\([^()]*, 32767\)"), "a Short \\ checks against 32767");
            Assert.That(js, Does.Match(@"__blNarrowDiv\([^()]*, 127\)"), "an SByte \\ checks against 127");
        });
    }
}

/// <summary>The run half: C# (real .NET), C++ and JavaScript, all through the optimizer.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class NarrowIntegerDivisionRunTests
{
    private static string Norm(string s) => FourBackends.Norm(s);

    [Test]
    public void NarrowIntegerDivision_CSharpReference()
        => Assert.That(Norm(FourBackends.RunEmittedCSharp(NarrowIntegerDivisionTests.Program)),
            Is.EqualTo(NarrowIntegerDivisionTests.Expected));

    [Test]
    public void NarrowIntegerDivision_Cpp()
        => Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(NarrowIntegerDivisionTests.Program))),
            Is.EqualTo(NarrowIntegerDivisionTests.Expected));

    // CompileOptimized, NOT JavaScriptExecutionTests.RunJs — that one runs no optimizer.
    [Test]
    public void NarrowIntegerDivision_JavaScript()
        => Assert.That(Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(NarrowIntegerDivisionTests.Program))),
            Is.EqualTo(NarrowIntegerDivisionTests.Expected));
}
