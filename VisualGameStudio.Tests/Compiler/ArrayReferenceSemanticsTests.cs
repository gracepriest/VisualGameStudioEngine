using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A BasicLang array is a .NET array: a REFERENCE. Assigning it, passing it, storing it in a
/// List, a field or a Structure, or returning it shares ONE array; only <c>ReDim</c> or assigning
/// a new array detaches.
///
/// <para>⛔ C++ lowered an array to a bare <c>std::vector</c> — a VALUE — so every one of those
/// copied the storage: <c>b = a : b(0) = 99</c> left <c>a(0)</c> alone, a Sub that wrote its
/// array argument changed nothing the caller saw, and <c>lst.Add(a)</c> stored a snapshot. C#
/// and JavaScript printed the .NET answer from the same program. Arrays now lower to
/// <c>BasicLang::Array&lt;T&gt;</c>, a handle to shared storage (CppArrayRuntime).</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node, a C++ compiler and dotnet
[NonParallelizable]
public class ArrayReferenceSemanticsExecutionTests
{
    /// <summary>Everything JavaScript can express (it refuses ByRef and Structure by design).</summary>
    private const string Program = """
        Class Holder
            Public Data() As Integer
        End Class

        Sub Poke(v() As Integer)
            v(0) = 77
        End Sub

        Sub Replace(v() As Integer)
            v = {5, 5, 5}
            v(0) = 6
        End Sub

        Sub Grow(v() As Integer)
            ReDim v[5]
            v(0) = 1234
        End Sub

        Function Same(v() As Integer) As Integer()
            Return v
        End Function

        Sub Main()
            Dim a[] As Integer = {1, 2, 3}
            Dim b() As Integer = a
            b(0) = 99
            Console.WriteLine(a(0))
            Poke(a)
            Console.WriteLine(a(0))
            Replace(a)
            Console.WriteLine(a(0))
            Grow(a)
            Console.WriteLine(CStr(a(0)) & " " & CStr(a.Length))
            Dim lst As New List(Of Integer())()
            lst.Add(a)
            a(1) = 42
            Console.WriteLine(lst(0)(1))
            Dim h As New Holder()
            h.Data = a
            h.Data(2) = 7
            Console.WriteLine(a(2))
            Dim c() As Integer = Same(a)
            c(0) = 11
            Console.WriteLine(a(0))
            Dim keep() As Integer = a
            ReDim Preserve a[4]
            a(0) = 0
            Console.WriteLine(CStr(keep(0)) & " " & CStr(keep.Length) & " " & CStr(a.Length))
            Dim g(1, 1) As Integer
            Dim g2(,) As Integer = g
            g2(1, 1) = 9
            Console.WriteLine(g(1, 1))
            Dim t As Integer = 0
            For Each x As Integer In keep
                t = t + x
            Next
            Console.WriteLine(t)
        End Sub
        """;

    // alias 99; Sub write 77; reassigned / ReDim'd parameter leaves the caller's array; List,
    // field and return all alias; ReDim Preserve detaches `a` from `keep`; a rank-2 alias; the
    // sum of keep = 11 + 42 + 7.
    private const string Expected = "99\n77\n77\n77 3\n42\n7\n11\n11 3 4\n9\n60";

    /// <summary>ByRef rebinds the caller's variable; a Structure copy copies the REFERENCE.</summary>
    private const string ByRefAndStructure = """
        Structure Pair
            Public Values() As Integer
        End Structure

        Sub ReplaceByRef(ByRef v() As Integer)
            v = {8, 8}
        End Sub

        Sub Main()
            Dim a[] As Integer = {1, 2, 3}
            Dim p As Pair
            p.Values = a
            Dim q As Pair = p
            q.Values(1) = 55
            Console.WriteLine(a(1))
            ReplaceByRef(a)
            Console.WriteLine(CStr(a(0)) & " " & CStr(a.Length))
        End Sub
        """;

    private const string ByRefAndStructureExpected = "55\n8 2";

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd('\n');

    private static string RunCpp(string program)
    {
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(InterpolatedStringLoweringTests.Optimized(program));
        return Normalize(VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(cpp, compiler.Value));
    }

    [Test]
    public void OnCpp() => Assert.That(RunCpp(Program), Is.EqualTo(Expected));

    [Test]
    public void OnCSharp() =>
        Assert.That(Normalize(CliTestHarness.CompileRunCSharp(Program)), Is.EqualTo(Expected));

    [Test]
    public void OnJavaScript() =>
        Assert.That(Normalize(JavaScriptExecutionTests.RunJs(Program)), Is.EqualTo(Expected));

    [Test]
    public void OnJavaScript_Optimized() =>
        Assert.That(Normalize(JavaScriptOptimizedExecutionTests.RunOptimized(Program)), Is.EqualTo(Expected));

    [Test]
    public void ByRefAndStructure_OnCpp() =>
        Assert.That(RunCpp(ByRefAndStructure), Is.EqualTo(ByRefAndStructureExpected));

    [Test]
    public void ByRefAndStructure_OnCSharp() =>
        Assert.That(Normalize(CliTestHarness.CompileRunCSharp(ByRefAndStructure)), Is.EqualTo(ByRefAndStructureExpected));
}
