using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>ParamArray</c>: any number of trailing arguments — or one array passed whole — arrive in
/// the callee as ONE array.
///
/// <para>⛔ Only C# worked, and only because csc packs <c>params</c> itself: <c>Sum(1, 2, 3)</c> was
/// "could not convert '1' from 'int' to 'std::vector&lt;int&gt;'" on C++ and printed
/// <c>undefined</c> on JavaScript. The analyzer refused the array-passed-whole form
/// (<c>Sum(arr)</c>: "cannot convert Integer[] to Integer"), and a ParamArray CONSTRUCTOR bound to
/// nothing ("No constructor for 'Bag' takes 3 argument(s)"). IRBuilder now packs the arguments
/// at the call site, so every backend receives the declared shape.</para>
/// </summary>
[TestFixture]
public class ParamArrayTests
{
    private const string Declarations = """
        Class Bag
            Public Sub New(ParamArray names() As String)
            End Sub
        End Class
        Function Sum(ParamArray v() As Integer) As Integer
            Return v.Length
        End Function

        """;

    private static string Errors(string body)
    {
        var parser = new Parser(new Lexer(Declarations + "Sub Main()\n    " + body + "\nEnd Sub").Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty);
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return string.Join("; ", analyzer.Errors.Select(e => e.Message));
    }

    [TestCase("Dim n = Sum(1, 2, 3)")]
    [TestCase("Dim n = Sum()")]
    [TestCase("Dim a[] As Integer = {1, 2}\n    Dim n = Sum(a)")]
    [TestCase("Dim n = Sum({4, 5})")]
    [TestCase("Dim b As New Bag(\"x\", \"y\", \"z\")")]
    [TestCase("Dim b As New Bag()")]
    public void EveryCallShape_Analyzes(string body)
        => Assert.That(Errors(body), Is.Empty);

    [TestCase("Dim n = Sum(\"a\")", "cannot convert from 'String' to 'Integer'")]
    [TestCase("Dim n = Sum(1, \"b\")", "Argument 2")]
    [TestCase("Dim b As New Bag(1, 2)", "not compatible")]
    [TestCase("Dim n = Sum(New String() {\"x\"})", "cannot convert from 'String[]' to 'Integer[]'")]
    public void AMismatchedElement_IsStillRefused(string body, string error)
        => Assert.That(Errors(body), Does.Contain(error));
}

[TestFixture]
[Category("Integration")]   // spawns node, a C++ compiler and dotnet
[NonParallelizable]
public class ParamArrayExecutionTests
{
    private const string Program = """
        Class Bag
            Public Items() As String
            Public Sub New(ParamArray names() As String)
                Items = names
            End Sub
            Public Function Total(ParamArray v() As Double) As Double
                Dim t As Double = 0
                For Each x As Double In v
                    t = t + x
                Next
                Return t
            End Function
            Public Shared Function Longest(ParamArray v() As String) As Integer
                Dim best As Integer = 0
                For Each s As String In v
                    If s.Length > best Then best = s.Length
                Next
                Return best
            End Function
        End Class

        Function Sum(ParamArray v[] As Integer) As Integer
            Dim t As Integer = 0
            For Each x As Integer In v
                t = t + x
            Next
            Return t
        End Function

        Function Tag(prefix As String, ParamArray v() As Integer) As String
            Return prefix & CStr(v.Length)
        End Function

        Sub Main()
            Console.WriteLine(Sum(1, 2, 3))
            Console.WriteLine(Sum())
            Console.WriteLine(Sum(5))
            Dim arr[] As Integer = {4, 5}
            Console.WriteLine(Sum(arr))
            Console.WriteLine(Sum({4, 5, 6}))
            Console.WriteLine(Tag("n", 7, 8, 9))
            Console.WriteLine(Tag("z"))
            Dim n As Integer = 10
            Console.WriteLine(Sum(n, n * 2, Sum(1, 1)))
            Dim b As New Bag("x", "yy", "zzz")
            Console.WriteLine(b.Items.Length)
            Dim e As New Bag()
            Console.WriteLine(e.Items.Length)
            Console.WriteLine(b.Total(1, 2.5, 3))
            Console.WriteLine(Bag.Longest("ab", "abcd", "a"))
        End Sub
        """;

    private const string Expected = "6\n0\n5\n9\n15\nn3\nz0\n32\n3\n0\n6.5\n4";

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd('\n');

    [Test]
    public void OnJavaScript() =>
        Assert.That(Normalize(JavaScriptExecutionTests.RunJs(Program)), Is.EqualTo(Expected));

    [Test]
    public void OnJavaScript_Optimized() =>
        Assert.That(Normalize(JavaScriptOptimizedExecutionTests.RunOptimized(Program)), Is.EqualTo(Expected));

    [Test]
    public void OnCpp()
    {
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(InterpolatedStringLoweringTests.Optimized(Program));
        Assert.That(Normalize(VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(cpp, compiler.Value)),
            Is.EqualTo(Expected));
    }

    [Test]
    public void OnCSharp() =>
        Assert.That(Normalize(CliTestHarness.CompileRunCSharp(Program)), Is.EqualTo(Expected));
}
