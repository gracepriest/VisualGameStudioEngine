using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Short / SByte / Byte / UShort <c>+ - *</c> and negation that leave the type's range must WRAP
/// to it on every backend, as Integer arithmetic already does on all of them.
///
/// <para><b>MEASURED on master</b> (<c>e69ec64e</c>), C++ wrapping and the other two not:</para>
/// <code>
///   JavaScript  32767 + 1 as Short printed 32768, 32767 * 2 printed 65534, -32767 - 2 printed
///               -32769, SByte 127 + 1 printed 128: the int32 `| 0` wrap is 32 bits wide.
///   C#          `Dim lo As Short = -s` did not BUILD (CS0266): C# promotes the operand of a
///               unary minus to int, the twin of the binary case NarrowIntegerDivisionTests pins.
/// </code>
/// <para>Wrapping, not OverflowException, because that is this compiler's Integer semantics on
/// every backend (see JavaScriptBackend.RenderBinary): VB.NET's checked Integer arithmetic is a
/// separate language decision. JavaScript now sign-extends / masks the int32 result to the narrow
/// width; C# casts a narrow negation back, unchecked. JavaScript's Integer negation of the
/// minimum also printed 2147483648 and now wraps.</para>
/// </summary>
[TestFixture]
public class NarrowIntegerWrapTests
{
    internal const string Program = @"
Function MaxShort() As Short
    Return 32767
End Function

Function MaxSByte() As SByte
    Return 127
End Function

Function MaxByte() As Byte
    Return 255
End Function

Function MaxUShort() As UShort
    Return 65535
End Function

Function MinInt() As Integer
    Return -2147483647 - 1
End Function

Sub Main()
    Dim s As Short = MaxShort()
    Dim one As Short = 1
    Dim two As Short = 2
    Dim sAdd As Short = s + one
    Dim sMul As Short = s * two
    Dim lo As Short = -s
    Dim sSub As Short = lo - two
    Dim sNeg As Short = -sAdd
    Console.WriteLine(sAdd & "" "" & sMul & "" "" & sSub & "" "" & sNeg)

    Dim b As SByte = MaxSByte()
    Dim b1 As SByte = 1
    Dim bAdd As SByte = b + b1
    Dim bMul As SByte = b * b
    Console.WriteLine(bAdd & "" "" & bMul)

    Dim u As Byte = MaxByte()
    Dim u1 As Byte = 1
    Dim uAdd As Byte = u + u1
    Dim uMul As Byte = u * u
    Console.WriteLine(uAdd & "" "" & uMul)

    Dim w As UShort = MaxUShort()
    Dim w1 As UShort = 1
    Dim wAdd As UShort = w + w1
    Console.WriteLine(wAdd)

    Dim m As Integer = MinInt()
    Dim mNeg As Integer = -m
    Console.WriteLine(mNeg)

    Dim small As Short = 100
    Dim fine As Short = small * two - one
    Console.WriteLine(fine)
End Sub
";

    internal const string Expected =
        "-32768 -2 32767 -32768\n" +
        "-128 1\n" +
        "0 1\n" +
        "0\n" +
        "-2147483648\n" +
        "199";

    [Test]
    public void CSharp_NarrowNegation_Builds()
    {
        var errors = ReturnCoercionTests.CompileEmittedCSharpForTest(Program);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
    }

    [Test]
    public void Js_NarrowArithmetic_WrapsToTheNarrowWidth()
    {
        var js = JsTestSupport.CompileOptimized(Program);
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("<< 16) >> 16)"), "a Short result is sign-extended from 16 bits");
            Assert.That(js, Does.Contain("<< 24) >> 24)"), "an SByte result is sign-extended from 8 bits");
            Assert.That(js, Does.Contain("& 0xFF)"), "a Byte result is masked to 8 bits");
            Assert.That(js, Does.Contain("& 0xFFFF)"), "a UShort result is masked to 16 bits");
        });
    }

    [Test]
    public void Js_IntegerArithmetic_KeepsThePlainInt32Wrap()
    {
        var js = JsTestSupport.CompileOptimized(
            "Function Seven() As Integer\n    Return 7\nEnd Function\n" +
            "Sub Main()\n    Dim a As Integer = Seven()\n    Console.WriteLine(a + a)\nEnd Sub\n");
        Assert.That(js, Does.Contain("| 0)").And.Not.Contain("<< 16").And.Not.Contain("& 0xFF"));
    }
}

/// <summary>The run half: C# (real .NET), C++ and JavaScript, all through the optimizer.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class NarrowIntegerWrapRunTests
{
    private static string Norm(string s) => FourBackends.Norm(s);

    [Test]
    public void NarrowIntegerWrap_CSharpReference()
        => Assert.That(Norm(FourBackends.RunEmittedCSharp(NarrowIntegerWrapTests.Program)),
            Is.EqualTo(NarrowIntegerWrapTests.Expected));

    [Test]
    public void NarrowIntegerWrap_Cpp()
        => Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(NarrowIntegerWrapTests.Program))),
            Is.EqualTo(NarrowIntegerWrapTests.Expected));

    // CompileOptimized, NOT JavaScriptExecutionTests.RunJs — that one runs no optimizer.
    [Test]
    public void NarrowIntegerWrap_JavaScript()
        => Assert.That(Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(NarrowIntegerWrapTests.Program))),
            Is.EqualTo(NarrowIntegerWrapTests.Expected));
}
