using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using VisualGameStudio.Tests.Native;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>List.Sort()</c> and <c>List.Sort(comparison)</c> on every backend.
///
/// <para><b>MEASURED on master</b> (<c>6281c9a5</c>): the C++ backend did not compile any
/// <c>xs.Sort()</c> — <c>BasicLang::List</c> had no <c>Sort</c> member (the gap docs/HANDOFF.md
/// lists as <c>task_e7c50371</c>) — and, once it had one, the analyzer typed the call as Object, so
/// the void result was stored in a temp ("void value not ignored as it ought to be"). Separately,
/// JavaScript's <c>Sort()</c> on Doubles left a NaN where it was (<c>a - b</c> is NaN for any pair
/// holding one); .NET sorts NaN first.</para>
///
/// <para>⚠ String order is ORDINAL on C++, as it already was on JavaScript. .NET's default is
/// culture-aware: the two agree for the strings below (all one case, no accents) but not for
/// "apple" vs "Banana" — a known divergence, not pinned as correct.</para>
/// </summary>
[TestFixture]
public class ListSortTests
{
    internal const string Program = @"
Function Zero() As Double
    Return 0.0
End Function

Function Desc(a As Integer, b As Integer) As Integer
    Return b - a
End Function

Sub Show(label As String, xs As List(Of Integer))
    Dim line As String = label
    For Each x As Integer In xs
        line = line & "" "" & x
    Next
    Console.WriteLine(line)
End Sub

Sub Main()
    Dim xs As New List(Of Integer)
    xs.Add(5)
    xs.Add(-2)
    xs.Add(9)
    xs.Add(1)
    xs.Add(5)
    xs.Sort()
    Show(""asc"", xs)
    xs.Sort(AddressOf Desc)
    Show(""desc"", xs)
    xs.Sort(Function(a As Integer, b As Integer) a - b)
    Show(""lambda"", xs)
    Console.WriteLine(xs.IndexOf(9))

    Dim empty As New List(Of Integer)
    empty.Sort()
    Console.WriteLine(""empty "" & empty.Count)

    Dim names As New List(Of String)
    names.Add(""pear"")
    names.Add(""fig"")
    names.Add(""apple"")
    names.Add(""kiwi"")
    names.Sort()
    Dim joined As String = """"
    For Each n As String In names
        joined = joined & n & "";""
    Next
    Console.WriteLine(joined)

    Dim ds As New List(Of Double)
    ds.Add(2.5)
    ds.Add(Zero() / Zero())
    ds.Add(-1.0)
    ds.Add(0.5)
    ds.Sort()
    For Each d As Double In ds
        Console.WriteLine(d)
    Next
End Sub
";

    internal const string Expected =
        "asc -2 1 5 5 9\n" +
        "desc 9 5 5 1 -2\n" +
        "lambda -2 1 5 5 9\n" +
        "4\n" +
        "empty 0\n" +
        "apple;fig;kiwi;pear;\n" +
        "NaN\n-1\n0.5\n2.5";

    [Test]
    public void Cpp_SortIsAStatement_NotAStoredValue()
    {
        var cpp = CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(Program));
        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("xs->Sort();"));
            Assert.That(cpp, Does.Not.Match(@"= \w+->Sort\("), "a void Sort() was stored in a temp");
        });
    }

    [Test]
    public void Js_DoubleSort_PutsNaNFirst()
        => Assert.That(JsTestSupport.CompileOptimized(Program),
            Does.Contain(".sort((a, b) => a !== a ? (b !== b ? 0 : -1) : b !== b ? 1 : a - b)"));

    /// <summary>
    /// A class element has no default order in .NET (Sort throws InvalidOperationException). On C++
    /// its <c>shared_ptr</c> WOULD compare — by address — so the runtime refuses it at compile time.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void CppRuntime_SortOfAClassElement_WithoutAComparison_DoesNotCompile()
    {
        var compiler = CppCompile.FindRunCompiler();
        if (compiler is null)
            Assert.Ignore("No C++ compiler (clang++/g++/MSVC) available.");

        var program =
            "#include <vector>\n#include <unordered_map>\n#include <unordered_set>\n#include <algorithm>\n" +
            "#include <stdexcept>\n#include <cstdint>\n#include <string>\n#include <memory>\n#include <iostream>\n\n" +
            CppCollectionsRuntime.Source +
            "\nstruct Box { int v; };\n" +
            "int main() {\n" +
            "    BasicLang::List<std::shared_ptr<Box>> boxes;\n" +
            "    boxes.Sort();\n" +
            "    return 0;\n" +
            "}\n";

        var (compiled, output) = CppCompile.TryCompile(program, compiler!.Value);
        Assert.That(compiled, Is.False, "sorting shared_ptr elements by address must not compile");
        Assert.That(output, Does.Contain("default order"), output);
    }

    [Test]
    [Category("Integration")]
    public void CppRuntime_SortOrdersNumbersNaNFirstAndHonoursAComparison()
    {
        var compiler = CppCompile.FindRunCompiler();
        if (compiler is null)
            Assert.Ignore("No C++ compiler (clang++/g++/MSVC) available.");

        var program =
            "#include <vector>\n#include <unordered_map>\n#include <unordered_set>\n#include <algorithm>\n" +
            "#include <stdexcept>\n#include <cstdint>\n#include <string>\n#include <memory>\n#include <iostream>\n" +
            "#include <limits>\n\n" +
            CppCollectionsRuntime.Source +
            "\nint main() {\n" +
            "    BasicLang::List<double> ds;\n" +
            "    ds.Add(2.5); ds.Add(std::numeric_limits<double>::quiet_NaN()); ds.Add(-1.0);\n" +
            "    ds.Add(std::numeric_limits<double>::quiet_NaN()); ds.Add(0.5);\n" +
            "    ds.Sort();\n" +
            "    for (double d : ds) std::cout << (d != d ? std::string(\"nan\") : std::to_string(d)) << \" \";\n" +
            "    std::cout << \"\\n\";\n" +
            "    BasicLang::List<std::string> ss;\n" +
            "    ss.Add(\"b\"); ss.Add(\"a\"); ss.Add(\"c\");\n" +
            "    ss.Sort([](const std::string& a, const std::string& b) { return a < b ? 1 : (a > b ? -1 : 0); });\n" +
            "    for (const auto& s : ss) std::cout << s;\n" +
            "    std::cout << \"\\n\";\n" +
            "    return 0;\n" +
            "}\n";

        var stdout = CppCompile.CompileAndRun(program, compiler!.Value).Replace("\r\n", "\n");
        Assert.That(stdout, Is.EqualTo("nan nan -1.000000 0.500000 2.500000 \ncba\n"));
    }
}

/// <summary>The run half: C# (real .NET), C++ and JavaScript, all through the optimizer.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class ListSortRunTests
{
    private static string Norm(string s) => FourBackends.Norm(s);

    [Test]
    public void ListSort_CSharpReference()
        => Assert.That(Norm(FourBackends.RunEmittedCSharp(ListSortTests.Program)), Is.EqualTo(ListSortTests.Expected));

    [Test]
    public void ListSort_Cpp()
        => Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(ListSortTests.Program))),
            Is.EqualTo(ListSortTests.Expected));

    // CompileOptimized, NOT JavaScriptExecutionTests.RunJs — that one runs no optimizer.
    [Test]
    public void ListSort_JavaScript()
        => Assert.That(Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(ListSortTests.Program))),
            Is.EqualTo(ListSortTests.Expected));
}
