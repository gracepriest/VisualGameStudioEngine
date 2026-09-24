using System.Text.RegularExpressions;
using NUnit.Framework;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.JavaScript;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A non-String interpolation hole lowers the way <c>&amp;</c> does: straight into a Concat.
///
/// <para>It used to lower to a call to a free function named <c>ToString</c>, which no backend
/// defines, so <c>$"{n}"</c> with an Integer failed to build on C#, C++ and JavaScript
/// alike.</para>
/// </summary>
[TestFixture]
public class InterpolatedStringLoweringTests
{
    internal const string Program = @"
Function Twice(x As Integer) As Integer
    Return x * 2
End Function

Sub Main()
    Dim n As Integer = 7
    Dim m As Integer = 5
    Dim s As String = ""S""
    Console.WriteLine($""n={n} m={m}"")
    Console.WriteLine($""{n}"")
    Console.WriteLine($""{n}{m}"")
    Console.WriteLine($""{n + m} and {Twice(n)}"")
    Console.WriteLine($""{s}{n}"")
    Dim t As String = $""{n}""
    Console.WriteLine(Len(t))
End Sub";

    /// <summary>What .NET prints for <see cref="Program"/>.</summary>
    internal const string Expected = "n=7 m=5\n7\n75\n12 and 14\nS7\n1\n";

    // Through the optimizer: the IR that actually ships.
    internal static BasicLang.Compiler.IR.IRModule Optimized(string source)
    {
        var module = JsTestSupport.BuildModule(source);
        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(module);
        return module;
    }

    // The old emission: a FREE ToString applied to one of the program's values — `ToString(n)`
    // on C#, `t0 = ToString(n)` on C++, `const t1 = ToString(n)` on JS. (Not the C++ prelude's
    // own `ToString()` / `ToString("fmt")` members, which are legitimate.)
    private static readonly Regex FreeToStringCall = new(@"(?<![.\w])ToString\((?:n|m|t\d+)\)");

    [Test]
    public void NoBackend_IsHandedAFreeToStringCall()
    {
        var module = Optimized(Program);
        var cs = new ImprovedCSharpCodeGenerator(new CodeGenOptions { GenerateComments = false }).Generate(module);
        Assert.That(FreeToStringCall.IsMatch(cs), Is.False, "C#:\n" + cs);

        module = Optimized(Program);
        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(module);
        Assert.That(FreeToStringCall.IsMatch(cpp), Is.False, "C++");
        Assert.That(cpp, Does.Contain("std::to_string("), "C++ stringifies an Integer hole the way it does `&`");

        var js = JsTestSupport.CompileOptimized(Program);
        Assert.That(FreeToStringCall.IsMatch(js), Is.False, "JavaScript:\n" + js);
    }

    [Test]
    public void LeadingNonStringHole_StartsTheConcatFromAString()
    {
        // `{n}{m}` must concatenate, not add: every Concat needs a String on its left.
        var js = JsTestSupport.Compile("Sub Main()\nDim n As Integer = 7\nDim m As Integer = 5\n" +
                                       "Console.WriteLine($\"{n}{m}\")\nEnd Sub");
        Assert.That(js, Does.Contain("(\"\" + n)"), js);
    }
}

[TestFixture]
[Category("Integration")]   // spawns node and a C++ compiler
[NonParallelizable]
public class InterpolatedStringExecutionTests
{
    [Test]
    public void JavaScript_PrintsWhatDotNetPrints()
    {
        var stdout = JavaScriptOptimizedExecutionTests.RunOptimized(InterpolatedStringLoweringTests.Program);
        // RunOptimized trims the trailing newline.
        Assert.That(stdout.Replace("\r\n", "\n").TrimEnd('\n'),
            Is.EqualTo(InterpolatedStringLoweringTests.Expected.TrimEnd('\n')));
    }

    [Test]
    public void Cpp_PrintsWhatDotNetPrints()
    {
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(InterpolatedStringLoweringTests.Optimized(InterpolatedStringLoweringTests.Program));
        var stdout = VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(cpp, compiler.Value);
        Assert.That(stdout.Replace("\r\n", "\n").TrimEnd('\n'),
            Is.EqualTo(InterpolatedStringLoweringTests.Expected.TrimEnd('\n')));
    }
}
