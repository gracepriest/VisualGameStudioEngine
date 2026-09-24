using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A user variable named like a compiler temp (<c>t1</c>, <c>t2</c>, ...) used to share its name
/// with an unrelated temp, and the optimizer and backends confused the two. MEASURED on master
/// 883fb1d with <see cref="IntegerProgram"/>, which must print 13:
/// <list type="bullet">
/// <item>CSE merged the store to <c>t2</c> into <c>t1</c>'s and deleted it, and constant folding
/// deleted the store to <c>t3</c> — both passes guessed "temp" from the name shape;</item>
/// <item>C# emitted <c>t3 = t1 + t2</c>: the Return's own temp was also called t3. It printed 10;
/// JavaScript printed 10 too;</item>
/// <item>C++ minted its own temp <c>t1</c> beside the user's local — "redeclaration of 'int32_t t1'".</item>
/// </list>
/// With Single locals <c>t1..t4</c>, the <c>Console.WriteLine</c> call's temp was named t4 and
/// C# emitted <c>t4 = Console.WriteLine(...)</c>. Every program goes THROUGH the optimizer.
/// </summary>
public class TempNameCollisionTests
{
    internal const string IntegerProgram = @"
Function F(n As Integer) As Integer
    Dim t1 As Integer = n + 1
    Dim t2 As Integer = n + 1
    Dim t3 As Integer = 6 \ 2
    Return t1 + t2 + t3
End Function
Sub Main()
    Console.WriteLine(CStr(F(4)))
End Sub
";

    /// <summary>Singles, a temp-shaped PARAMETER and a temp-shaped module GLOBAL.</summary>
    internal const string MixedProgram = @"
Dim t5 As Integer = 100
Function G(t0 As Integer) As Integer
    Return t0 * 3 + t0 + t5
End Function
Sub Check(tag As String, ok As Boolean)
    If ok Then
        Console.WriteLine(tag & "" ok"")
    Else
        Console.WriteLine(tag & "" BAD"")
    End If
End Sub
Sub Main()
    Dim a As Single = 1.0F
    Dim t1 As Single = a / 2.0F
    Dim t2 As Single = 1.0F / 4.0F
    Dim t3 As Single = 3.0F / 2.0F
    Dim t4 As Single = 1.5F + 0.0F
    Check(""single"", t1 + t2 + t3 + t4 = 3.75F)
    Check(""param"", G(5) = 120)
End Sub
";

    internal const string MixedExpected = "single ok\nparam ok";

    [Test]
    public void CSharp_EveryStoreSurvives_AndTheCodeCompiles()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(IntegerProgram);
        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Match(@"\bt2 = (n \+ 1|t1);"), "the store to t2 was deleted:\n" + cs);
            Assert.That(cs, Does.Contain("t3 = 3;"), "the store to t3 was deleted:\n" + cs);
            Assert.That(cs, Does.Not.Contain("t3 = t1 + t2"), "a temp wrote into the user's t3:\n" + cs);
        });
        Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(IntegerProgram), Is.Empty);
        Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(MixedProgram), Is.Empty,
            "it was `t4 = Console.WriteLine(...)`");
    }

    [Test]
    public void JavaScript_EveryStoreSurvives()
    {
        var js = JsTestSupport.CompileOptimized(IntegerProgram);
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Match(@"\bt2 = (\(?n \+ 1\)?|t1)"), "the store to t2 was deleted:\n" + js);
            Assert.That(js, Does.Match(@"\bt3 = 3\b"), "the store to t3 was deleted:\n" + js);
        });
    }

    [Test]
    public void Cpp_NoTempRedeclaresAUserLocal()
    {
        var cpp = CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(IntegerProgram));
        foreach (var name in new[] { "t1", "t2", "t3" })
            Assert.That(Regex.Matches(cpp, $@"\bint32_t {name} =").Count, Is.EqualTo(1),
                $"{name} declared more than once:\n" + cpp);
    }

    /// <summary>A program with no temp-shaped names is untouched by any of this.</summary>
    [Test]
    public void ProgramWithoutTempShapedNames_KeepsItsTempNames()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(IntegerProgram.Replace("t1", "v1").Replace("t2", "v2").Replace("t3", "v3"));
        Assert.That(cs, Does.Contain("v2 = v1;").Or.Contain("v2 = n + 1;"), cs);
    }
}

/// <summary>The same programs compiled AND run on each backend.</summary>
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class TempNameCollisionRunTests
{
    [Test]
    public void CSharp_Runs() => Assert.Multiple(() =>
    {
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(TempNameCollisionTests.IntegerProgram)), Is.EqualTo("13"));
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(TempNameCollisionTests.MixedProgram)),
            Is.EqualTo(TempNameCollisionTests.MixedExpected));
    });

    [Test]
    public void Cpp_Runs() => Assert.Multiple(() =>
    {
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(TempNameCollisionTests.IntegerProgram))),
            Is.EqualTo("13"));
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(TempNameCollisionTests.MixedProgram))),
            Is.EqualTo(TempNameCollisionTests.MixedExpected));
    });

    /// <summary>Through <see cref="JsTestSupport.CompileOptimized"/> — <c>RunJs</c> skips the optimizer.</summary>
    [Test]
    public void JavaScript_Runs() => Assert.Multiple(() =>
    {
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(TempNameCollisionTests.IntegerProgram))),
            Is.EqualTo("13"));
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(TempNameCollisionTests.MixedProgram))),
            Is.EqualTo(TempNameCollisionTests.MixedExpected));
    });
}
