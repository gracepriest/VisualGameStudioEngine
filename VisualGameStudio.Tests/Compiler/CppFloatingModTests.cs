using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A floating <c>Mod</c> on the C++ backend lowers to <c>std::fmod</c>. It used to emit the bare
/// <c>%</c>, which C++ has no overload of for floating operands — MEASURED on master 21b4468:
/// <c>D(7.5) Mod D(2.0)</c> failed the C++ build with "invalid operands of types 'double' and
/// 'double' to binary 'operator%'", in both pipelines, while C# and JavaScript printed 1.5.
/// .NET's floating <c>%</c> is fmod: truncated toward zero, the sign of the dividend, NaN for a
/// zero divisor, exact.
/// </summary>
public class CppFloatingModTests
{
    internal const string Program = @"
Function D(v As Double) As Double
    Return v
End Function
Function S(v As Single) As Single
    Return v
End Function
Function I(v As Integer) As Integer
    Return v
End Function
Function Band(v As Double) As String
    Select Case v
        Case Is > 0 When v Mod 2.0 = 1.5
            Return ""odd-and-a-half""
        Case Else
            Return ""other""
    End Select
End Function
Sub Main()
    Console.WriteLine(D(7.5) Mod D(2.0))
    Console.WriteLine(D(-7.5) Mod D(2.0))
    Console.WriteLine(D(7.5) Mod D(-2.0))
    Console.WriteLine(D(5.0) Mod D(0.0))
    Console.WriteLine(D(-4.0) Mod D(2.0))
    Console.WriteLine(S(-7.25F) Mod S(2.0F))
    Console.WriteLine(D(7.5) Mod I(2))
    Console.WriteLine(I(7) Mod D(2.5))
    Dim x As Double = D(10.25)
    x = x Mod 3
    Console.WriteLine(x)
    Console.WriteLine(Band(D(7.5)))
    Console.WriteLine(Band(D(7.0)))
    Console.WriteLine(I(7) Mod I(3))
End Sub
";

    internal const string Expected =
        "1.5\n-1.5\n1.5\nNaN\n-0\n-1.25\n1.5\n2\n1.25\nodd-and-a-half\nother\n1";

    [Test]
    public void FloatingMod_LowersToFmod_InStatementsAndInAWhenGuard()
    {
        var user = CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(Program));
        Assert.Multiple(() =>
        {
            Assert.That(user, Does.Contain("std::fmod("), user);
            // The guard renders inline, through RenderInline — the other emission path.
            Assert.That(user, Does.Contain("std::fmod(v, 2.0)"), user);
            Assert.That(user, Does.Not.Match(@"\b\w+ % \w+"), "a bare % on a floating operand does not compile:\n" + user);
            // The integral Mod keeps its checked helper.
            Assert.That(user, Does.Contain("BasicLang::IntMod("), user);
        });
    }
}

/// <summary>The program compiled and run as C++, against C# as the reference.</summary>
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class CppFloatingModRunTests
{
    [Test]
    public void CSharp_Runs() =>
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(CppFloatingModTests.Program)),
            Is.EqualTo(CppFloatingModTests.Expected));

    [Test]
    public void Cpp_Runs() =>
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(CppFloatingModTests.Program))),
            Is.EqualTo(CppFloatingModTests.Expected));

    [Test]
    public void Cpp_Aggressive_Runs() =>
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(CppFloatingModTests.Program))),
            Is.EqualTo(CppFloatingModTests.Expected));
}
