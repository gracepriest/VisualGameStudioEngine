using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// An integral or Decimal division by zero must THROW at run time on the C# backend, as it does in
/// .NET/VB — not fail to build. Roslyn rejects a constant over a constant zero as CS0020 ("Division
/// by constant zero"), and the IR hands the emitter two constants far more often than a literal
/// <c>5 \ 0</c>, because copy propagation substitutes locals.
///
/// <para><b>MEASURED on master</b>, through the optimizer (the CLI route), each emitted as a
/// constant over a constant and rejected by Roslyn:</para>
/// <code>
///   5 \ 0                           q = 5 / 0;
///   5 Mod 0                         q = 5 % 0;
///   Dim z = 0 : 5 \ z               q = 5 / 0;
///   Dim a = 5 : a \ 0               q = 5 / 0;
///   Decimal: Dim d = 5 : d / 0      q = 5m / 0m;
/// </code>
/// <para>A VARIABLE over a constant zero (<c>n \ 0</c>) already compiled and threw; Roslyn raises
/// CS0020 only when both operands are constants. Floating division by zero is legal (Infinity)
/// and must be left alone.</para>
/// </summary>
[TestFixture]
public class CSharpConstantDivideByZeroTests
{
    internal static string Guarded(string name, string body) => $@"
Sub {name}()
    Try
        {body}
        Console.WriteLine(q)
    Catch ex As DivideByZeroException
        Console.WriteLine(""{name} dbz"")
    End Try
End Sub
";

    private static string Program(string body) => Guarded("Probe", body) + "\nSub Main()\n    Probe()\nEnd Sub\n";

    [TestCase("Dim q As Integer = 5 \\ 0")]
    [TestCase("Dim q As Integer = 5 Mod 0")]
    [TestCase("Dim q As Long = 5 \\ 0")]
    [TestCase("Dim q As Integer = 0 \\ 0")]
    [TestCase("Dim z As Integer = 0\n        Dim q As Integer = 5 \\ z")]
    [TestCase("Dim a As Integer = 5\n        Dim q As Integer = a \\ 0")]
    [TestCase("Dim d As Decimal = 5\n        Dim q As Decimal = d / 0")]
    public void ConstantOverConstantZero_Compiles(string body)
    {
        var errors = ReturnCoercionTests.CompileEmittedCSharpForTest(Program(body));
        Assert.That(errors, Is.Empty,
            "the emitted C# must compile and throw at run time, as .NET does:\n" + string.Join("\n", errors));
    }

    /// <summary>The guard against overshooting: a floating divisor is not rewritten.</summary>
    [Test]
    public void FloatingDivisionByZero_IsLeftAlone()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(
            "Sub Main()\n    Dim q As Double = 5 / 0\n    Console.WriteLine(q)\nEnd Sub\n");
        Assert.That(cs, Does.Contain("(double)(5) / (double)(0)"));
        Assert.That(cs, Does.Not.Contain("new[]"));
    }

    /// <summary>A variable dividend never needed the rewrite and must not get it.</summary>
    [Test]
    public void VariableOverConstantZero_IsLeftAlone()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(
            "Sub P(n As Integer)\n    Dim q As Integer = n \\ 0\n    Console.WriteLine(q)\nEnd Sub\n" +
            "Sub Main()\n    P(1)\nEnd Sub\n");
        Assert.That(cs, Does.Contain("n / "));
    }
}

/// <summary>
/// The run half of <see cref="CSharpConstantDivideByZeroTests"/>: the program compiled through the
/// optimizer and executed. Every case must reach its Catch; the Double control must print Infinity
/// (checked as "greater than 1E308", so no platform spelling is involved).
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class CSharpConstantDivideByZeroRunTests
{
    private static readonly (string Name, string Body)[] Cases =
    {
        ("LitIntDiv", "Dim q As Integer = 5 \\ 0"),
        ("LitMod", "Dim q As Integer = 5 Mod 0"),
        ("LitLong", "Dim q As Long = 5 \\ 0"),
        ("ZeroOverZero", "Dim q As Integer = 0 \\ 0"),
        ("PropDivisor", "Dim z As Integer = 0\n        Dim q As Integer = 5 \\ z"),
        ("PropDividend", "Dim a As Integer = 5\n        Dim q As Integer = a \\ 0"),
        ("PropDecimal", "Dim d As Decimal = 5\n        Dim q As Decimal = d / 0"),
    };

    [Test]
    public void EveryConstantDivisionByZero_ThrowsAtRunTime_CSharp()
    {
        var program = string.Concat(Cases.Select(c => CSharpConstantDivideByZeroTests.Guarded(c.Name, c.Body))) +
            "\nSub DoubleControl()\n    Dim q As Double = 5 / 0\n    Console.WriteLine(q > 1.0E+308)\nEnd Sub\n" +
            "\nSub Main()\n" + string.Concat(Cases.Select(c => $"    {c.Name}()\n")) + "    DoubleControl()\nEnd Sub\n";

        var expected = string.Join("\n", Cases.Select(c => $"{c.Name} dbz")) + "\nTrue";
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected));
    }
}
