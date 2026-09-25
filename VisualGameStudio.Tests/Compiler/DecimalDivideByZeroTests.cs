using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A Decimal <c>/</c> or <c>Mod</c> by zero on the C++ backend must throw what .NET throws: a
/// DivideByZeroException, matched by TYPE, with .NET's message.
///
/// <para><b>MEASURED on master</b>: the Decimal runtime threw a plain
/// <c>std::runtime_error("Decimal division by zero")</c>. Only a <c>NetException</c> enters the
/// typed-catch ladder; a plain runtime_error is taken by the FIRST per-clause handler, whatever
/// its type — so a <c>Catch ex As InvalidOperationException</c> nested inside a
/// <c>Catch ex As DivideByZeroException</c> printed its own body, where .NET (and the C#
/// backend) reach the outer handler. The message also differed from .NET's.</para>
///
/// <para>Fix: both throws are <c>NetException(DivideByZeroChain, "Attempted to divide by
/// zero.")</c>, and the NetException runtime is now spliced BEFORE the BCL bodies in both
/// emission modes so the Decimal body can name it. (JavaScript refuses Decimal by design,
/// BL7007.)</para>
/// </summary>
[TestFixture]
public class DecimalDivideByZeroTests
{
    [Test]
    public void DecimalRuntime_ThrowsTheTypedDivideByZeroException()
    {
        var body = CppDecimalRuntime.DecimalBody;
        Assert.That(body, Does.Not.Contain("Decimal division by zero"));
        Assert.That(body, Does.Contain("throw NetException(DivideByZeroChain, \"Attempted to divide by zero.\")"));
    }

    /// <summary>The Decimal body names NetException, so NetException must be spliced first.</summary>
    [Test]
    public void CombinedMode_SplicesNetExceptionBeforeTheDecimalRuntime()
    {
        var cpp = BclE2E.CompileToCppOptimized("Sub Main()\n    Console.WriteLine(1)\nEnd Sub\n").Replace("\r\n", "\n");
        var netAt = cpp.IndexOf("class NetException", System.StringComparison.Ordinal);
        var decAt = cpp.IndexOf("inline Decimal operator/(", System.StringComparison.Ordinal);
        Assert.That(netAt, Is.GreaterThanOrEqualTo(0));
        Assert.That(decAt, Is.GreaterThan(netAt), "the Decimal runtime would name NetException before it is declared");
    }

    /// <summary>The standalone bl_decimal.hpp (native vector tests) must stay self-contained.</summary>
    [Test]
    public void StandaloneDecimalHeader_DefinesNetExceptionBeforeItsBody()
    {
        var header = CppDecimalRuntime.DecimalHeader;
        var netAt = header.IndexOf("class NetException", System.StringComparison.Ordinal);
        var decAt = header.IndexOf("inline Decimal operator/(", System.StringComparison.Ordinal);
        Assert.That(netAt, Is.GreaterThanOrEqualTo(0), "bl_decimal.hpp no longer compiles on its own");
        Assert.That(decAt, Is.GreaterThan(netAt));
    }
}

/// <summary>The run half: C# (real .NET, the reference) and C++, through the optimizer.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class DecimalDivideByZeroRunTests
{
    private const string Program = @"
Sub AsDbz(d As Decimal, z As Decimal)
    Try
        Dim q As Decimal = d / z
        Console.WriteLine(q)
    Catch ex As DivideByZeroException
        Console.WriteLine(""div dbz: "" & ex.Message)
    End Try
End Sub

Sub ModAsArith(d As Decimal, z As Decimal)
    Try
        Dim q As Decimal = d Mod z
        Console.WriteLine(q)
    Catch ex As ArithmeticException
        Console.WriteLine(""mod arith"")
    End Try
End Sub

Sub NotAnInvalidOperation(d As Decimal, z As Decimal)
    Try
        Try
            Dim q As Decimal = d / z
            Console.WriteLine(q)
        Catch ex As InvalidOperationException
            Console.WriteLine(""WRONG: invalid-op clause took it"")
        End Try
    Catch ex As DivideByZeroException
        Console.WriteLine(""outer dbz"")
    End Try
End Sub

Sub Values(d As Decimal, z As Decimal)
    Console.WriteLine(d / z)
    Console.WriteLine(d Mod z)
End Sub

Sub Main()
    Dim seven As Decimal = 7
    Dim zero As Decimal = 0
    Dim two As Decimal = 2
    AsDbz(seven, zero)
    ModAsArith(seven, zero)
    NotAnInvalidOperation(seven, zero)
    Values(seven, two)
End Sub
";

    private const string Expected =
        "div dbz: Attempted to divide by zero.\nmod arith\nouter dbz\n3.5\n1";

    [Test]
    public void DecimalDivideByZero_CSharpReference()
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(Program)), Is.EqualTo(Expected));

    [Test]
    public void DecimalDivideByZero_IsTypedOnCpp()
        => Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(Program))), Is.EqualTo(Expected),
            "a 'WRONG:' line means a plain runtime_error reached the first typed handler");
}
