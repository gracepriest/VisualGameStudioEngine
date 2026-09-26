using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// An empty array literal takes its type from where it is used, as in VB. The analyzer typed a
/// literal from its elements, and <c>{}</c> has none, so it was always Object[]:
/// <c>Dim a() As Integer = {}</c> was refused with "Cannot assign value of type 'Object[]' to
/// variable of type 'Integer[]'", and so were <c>a = {}</c> and <c>Sum({})</c>.
/// </summary>
[TestFixture]
public class EmptyArrayLiteralTests
{
    private static string Errors(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        if (parser.Errors.Count > 0) return string.Join("; ", parser.Errors.Select(e => e.Message));
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return string.Join("; ", analyzer.Errors.Select(e => e.Message));
    }

    private const string Sum =
        "Function Sum(values[] As Integer) As Integer\n    Return values.Length\nEnd Function\n";

    [TestCase("Dim a() As Integer = {}")]
    [TestCase("Dim a[] As String = {}")]
    [TestCase("Dim a() As Double = {}")]
    [TestCase("Dim a() As Integer = {1}\n    a = {}")]
    [TestCase("Dim n As Integer = Sum({})")]
    [TestCase("Dim x = {}")]
    public void AnEmptyLiteral_TakesItsTargetsType(string body)
        => Assert.That(Errors(Sum + "Sub Main()\n    " + body + "\nEnd Sub"), Is.Empty);

    /// <summary>Only an EMPTY literal is target-typed, and only to an array.</summary>
    [TestCase("Dim i As Integer = {}", "Cannot assign")]
    [TestCase("Dim a() As Integer = {\"x\"}", "Cannot assign")]
    public void OtherMismatches_AreStillRefused(string body, string error)
        => Assert.That(Errors("Sub Main()\n    " + body + "\nEnd Sub"), Does.Contain(error));
}

[TestFixture]
[Category("Integration")]   // spawns node, a C++ compiler and dotnet
[NonParallelizable]
public class EmptyArrayLiteralExecutionTests
{
    private const string Program = @"
Function Sum(values[] As Integer) As Integer
    Dim t As Integer = 0
    For Each v As Integer In values
        t = t + v
    Next
    Return t
End Function

Class Bag
    Public Items() As String
    Public Sub New()
        Items = {}
    End Sub
End Class

Sub Main()
    Dim a() As Integer = {}
    Dim b[] As String = {}
    Dim d() As Double = {}
    Console.WriteLine(CStr(a.Length) & CStr(b.Length) & CStr(d.Length))
    Console.WriteLine(Sum({}))
    a = {5, 6}
    Console.WriteLine(Sum(a))
    a = {}
    Console.WriteLine(a.Length)
    ReDim Preserve a[2]
    a(1) = 9
    Console.WriteLine(Sum(a))
    Dim bag As New Bag()
    Console.WriteLine(bag.Items.Length)
    Dim n As Integer = 0
    For Each s As String In b
        n = n + 1
    Next
    Console.WriteLine(n)
End Sub";

    private const string Expected = "000\n0\n11\n0\n9\n0\n0";

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd('\n');

    [Test]
    public void OnJavaScript() =>
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
