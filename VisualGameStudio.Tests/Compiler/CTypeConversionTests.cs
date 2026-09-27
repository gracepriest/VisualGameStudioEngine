using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>CType</c>, <c>DirectCast</c>, <c>TryCast</c> and the conversion functions (<c>CInt</c>,
/// <c>CDbl</c>, <c>CStr</c>, <c>CBool</c>, …) with VB's meaning on every backend.
///
/// <para>⛔ No backend had it. JavaScript had no cast lowering at all ("IRCast lowering is not
/// implemented yet"), so any program with a <c>CType</c> failed its whole build. C++ lowered
/// <c>CType(x, Integer)</c> to a truncating <c>static_cast</c> — a compile error from a String —
/// and a class downcast to a <c>shared_ptr</c> constructor that does not exist. C# emitted
/// <c>(bool)1</c> (CS0030) and <c>(Dog)(a).Bark()</c>, which binds the call before the cast
/// (CS1061). And the conversion functions disagreed on VB's own rules: <c>CInt(True)</c> is -1
/// (every backend said 1), <c>CBool("0")</c> is False (C# threw, JavaScript said True).</para>
///
/// <para><c>CType</c> to a primitive now lowers to the matching conversion function
/// (IRBuilder.ConversionBuiltinFor), so the two spellings cannot disagree; those functions follow
/// VB's rules for a String or Boolean argument on each backend; and a reference cast checks the
/// runtime type — <c>TryCast</c> answers <c>Nothing</c>, <c>CType</c>/<c>DirectCast</c> throw.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node, a C++ compiler and dotnet
[NonParallelizable]
public class CTypeConversionExecutionTests
{
    private const string Program = """
        Class Animal
            Public Function Kind() As String
                Return "animal"
            End Function
        End Class

        Class Dog
            Inherits Animal
            Public Function Bark() As String
                Return "woof"
            End Function
        End Class

        Class Cat
            Inherits Animal
        End Class

        Sub Main()
            Dim t As Boolean = True
            Dim f As Boolean = False
            Dim d As Double = 7.6
            Console.WriteLine(CType(d, Integer))
            Console.WriteLine(CStr(CType(2.5, Integer)) & CStr(CType(3.5, Integer)))
            Console.WriteLine(CType("42", Integer) + 1)
            Console.WriteLine(CType("3.5", Integer))
            Console.WriteLine(CType(" 7 ", Integer))
            Console.WriteLine(CType(5, String) & "!")
            Console.WriteLine(CType(9, Double) / 2)
            Console.WriteLine(CDbl("1e3"))
            Console.WriteLine(CType(1, Boolean))
            Console.WriteLine(CBool(0.0))
            Console.WriteLine(CBool("False"))
            Console.WriteLine(CBool("0"))
            Console.WriteLine(CBool("true"))
            Console.WriteLine(CStr(t) & "|" & CStr(f))
            Console.WriteLine(CInt(t))
            Console.WriteLine(CType(t, Integer) + 10)
            Console.WriteLine(CDbl(f))
            Console.WriteLine(CInt(7.5) + CInt(8.5))

            Dim a As Animal = New Dog()
            Console.WriteLine(CType(a, Dog).Bark())
            Console.WriteLine(DirectCast(a, Dog).Bark())
            Dim up As Animal = CType(New Dog(), Animal)
            Console.WriteLine(up.Kind())

            Dim c As Animal = New Cat()
            Dim notDog As Dog = TryCast(c, Dog)
            If notDog = Nothing Then Console.WriteLine("TryCast: Nothing")
            Try
                Dim bad As Dog = CType(c, Dog)
                Console.WriteLine("no throw")
            Catch ex As Exception
                Console.WriteLine("CType: InvalidCast")
            End Try
            Dim none As Animal
            Dim stillNone As Dog = DirectCast(none, Dog)
            If stillNone = Nothing Then Console.WriteLine("DirectCast: Nothing")

            Dim n() As Integer = {7, 8}
            Dim back As Integer() = CType(n, Integer())
            Console.WriteLine(back(1))
        End Sub
        """;

    private const string Expected =
        "8\n24\n43\n4\n7\n5!\n4.5\n1000\nTrue\nFalse\nFalse\nFalse\nTrue\nTrue|False\n-1\n9\n0\n16\n"
        + "woof\nwoof\nanimal\nTryCast: Nothing\nCType: InvalidCast\nDirectCast: Nothing\n8";

    /// <summary>Long is banned on JavaScript (BL7003), so CLng runs on C# and C++ only.</summary>
    private const string LongProgram = """
        Sub Main()
            Console.WriteLine(CLng(2.5) + CLng(1))
            Console.WriteLine(CType("2.5", Long))
            Dim t As Boolean = True
            Console.WriteLine(CLng(t))
        End Sub
        """;

    private const string LongExpected = "3\n2\n-1";

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
    public void OnJavaScript() =>
        Assert.That(Normalize(JavaScriptExecutionTests.RunJs(Program)), Is.EqualTo(Expected));

    [Test]
    public void OnJavaScript_Optimized() =>
        Assert.That(Normalize(JavaScriptOptimizedExecutionTests.RunOptimized(Program)), Is.EqualTo(Expected));

    [Test]
    public void OnCpp() => Assert.That(RunCpp(Program), Is.EqualTo(Expected));

    [Test]
    public void OnCSharp() =>
        Assert.That(Normalize(CliTestHarness.CompileRunCSharp(Program)), Is.EqualTo(Expected));

    [Test]
    public void Long_OnCpp() => Assert.That(RunCpp(LongProgram), Is.EqualTo(LongExpected));

    [Test]
    public void Long_OnCSharp() =>
        Assert.That(Normalize(CliTestHarness.CompileRunCSharp(LongProgram)), Is.EqualTo(LongExpected));
}
