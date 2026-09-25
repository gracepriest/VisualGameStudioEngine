using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// An array literal, <c>{1, 2, 3}</c>, reaches every backend as one IRArrayAlloc followed by one
/// IRArrayStore per element, and an initialised array local also stores into its
/// <c>&lt;name&gt;_addr</c> slot (an IRAlloca). The JavaScript backend lowered none of the three,
/// so any program with an array literal failed its whole build: "IRArrayAlloc lowering is not
/// implemented", then "IRAlloca (as an expression)". C# and C++ were unaffected.
/// </summary>
[TestFixture]
public class JavaScriptArrayLiteralCodeGenTests
{
    private static string Js(string source) =>
        new BasicLang.Compiler.CodeGen.JavaScript.JavaScriptCodeGenerator().Generate(JsTestSupport.BuildModule(source));

    [Test]
    public void ALiteral_IsAFilledArray_ThenOneStorePerElement()
    {
        var js = Js("Sub Main()\n    Dim a[] As Integer = {4, 5}\n    Console.WriteLine(a(1))\nEnd Sub");
        Assert.That(js, Does.Match(@"const (\w+) = new Array\(2\)\.fill\(0\);"), js);
        Assert.That(js, Does.Match(@"\w+\[0\] = 4;"), js);
        Assert.That(js, Does.Match(@"\w+\[1\] = 5;"), js);
        Assert.That(js, Does.Not.Match(@"\b(\w+) = \1;"), "no self-assignment through the _addr slot:\n" + js);
    }

    [TestCase("Dim a() As Integer = {1, 2, 3}")]
    [TestCase("Dim a[] As Integer = {1, 2, 3}")]
    [TestCase("Dim a = {1, 2, 3}")]
    [TestCase("Dim a() As String = {\"x\", \"y\"}")]
    public void EveryDeclarationSpelling_Compiles(string declaration)
        => Assert.DoesNotThrow(() => Js("Sub Main()\n    " + declaration + "\n    Console.WriteLine(a.Length)\nEnd Sub"));
}

[TestFixture]
[Category("Integration")]   // spawns node, a C++ compiler and dotnet
[NonParallelizable]
public class JavaScriptArrayLiteralExecutionTests
{
    /// <summary>
    /// Each declaration spelling, a String and a Double literal, elements computed by calls, a
    /// literal as an argument, as a For Each source, re-created in a loop body, assigned to a
    /// module variable and to a field in a constructor, and an element overwritten.
    /// </summary>
    private const string Program = @"
Dim G() As Integer

Class Box
    Public Items() As String
    Public Sub New()
        Items = {""a"", ""b"", ""c""}
    End Sub
End Class

Function Sum(values[] As Integer) As Integer
    Dim t As Integer = 0
    For Each v As Integer In values
        t = t + v
    Next
    Return t
End Function

Function Seed() As Integer
    Return 7
End Function

Sub Main()
    Dim a() As Integer = {1, 2, 3}
    Dim b[] As Integer = {4, 5}
    Dim c = {6, 7, 8, 9}
    Dim s() As String = {""p"", ""q""}
    Dim d() As Double = {1.5, 2.5}
    Dim e() As Integer = {Seed(), Seed() + 1}
    Console.WriteLine(CStr(a.Length) & "" "" & CStr(a(0)) & CStr(a(1)) & CStr(a(2)))
    Console.WriteLine(CStr(b.Length) & "" "" & CStr(b(1)))
    Console.WriteLine(CStr(c.Length) & "" "" & CStr(c(3)))
    Console.WriteLine(s(0) & s(1))
    Console.WriteLine(d(0) + d(1))
    Console.WriteLine(CStr(e(0)) & "","" & CStr(e(1)))
    Console.WriteLine(Sum({10, 20, 30}))
    a(1) = 20
    Console.WriteLine(Sum(a))
    For i As Integer = 1 To 2
        Dim r() As Integer = {i, i * 10}
        Console.WriteLine(r(0) + r(1))
    Next
    For Each w As String In {""m"", ""n""}
        Console.Write(w)
    Next
    Console.WriteLine()
    G = {3, 4}
    Console.WriteLine(G(0) + G(1))
    Dim bx As New Box()
    Console.WriteLine(bx.Items(2) & CStr(bx.Items.Length))
End Sub";

    private const string Expected = "3 123\n2 5\n4 9\npq\n4\n7,8\n60\n24\n11\n22\nmn\n7\nc3";

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
