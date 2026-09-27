using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A method called through an INTERFACE-typed variable returns its declared type. The analyzer
/// visited an interface's methods but never registered them as MEMBERS of the interface type — the
/// method twin of the property gap fixed in InterfacePropertyMemberTypeTests — so every such call
/// typed as Object: <c>Dim t As String = s.Name()</c> was refused as Object→String and
/// <c>s.Area() + 1</c> as "Arithmetic operator '+' requires numeric operands". The interface was
/// usable only by casting back to the class.
/// </summary>
[TestFixture]
public class InterfaceMethodTypingTests
{
    private const string Shape = """
        Interface IShape
            Function Area() As Integer
            Function Name() As String
            Function Scale(k As Integer) As Double
            Sub Grow(by As Integer)
        End Interface

        """;

    private static string Errors(string body)
    {
        var parser = new Parser(new Lexer(Shape + "Sub Main()\n    Dim s As IShape\n    " + body + "\nEnd Sub").Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty);
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return string.Join("; ", analyzer.Errors.Select(e => e.Message));
    }

    [TestCase("Dim t As String = s.Name()")]
    [TestCase("Dim a As Integer = s.Area() + 1")]
    [TestCase("Dim d As Double = s.Scale(3) * 2")]
    [TestCase("s.Grow(1)")]
    public void AnInterfaceMethodCall_HasItsDeclaredType(string body)
        => Assert.That(Errors(body), Is.Empty);

    /// <summary>The member now carries its signature, so a bad call is refused where it is written.</summary>
    [TestCase("Dim d As Double = s.Scale(\"x\")", "cannot convert from 'String' to 'Integer'")]
    [TestCase("Dim d As Double = s.Scale(1, 2)", "expects 1 argument(s), got 2")]
    [TestCase("Dim t As String = s.Area()", "'Integer' to variable of type 'String'")]
    public void AMisuse_IsRefusedWithTheDeclaredSignature(string body, string error)
        => Assert.That(Errors(body), Does.Contain(error));
}

[TestFixture]
[Category("Integration")]   // spawns node, a C++ compiler and dotnet
[NonParallelizable]
public class InterfaceMethodTypingExecutionTests
{
    private const string Program = """
        Interface IShape
            Function Area() As Integer
            Function Name() As String
            Function Scale(k As Integer) As Double
            Sub Grow(by As Integer)
        End Interface

        Class Sq
            Implements IShape
            Private _s As Integer
            Public Sub New(s As Integer)
                _s = s
            End Sub
            Public Function Area() As Integer Implements IShape.Area
                Return _s * _s
            End Function
            Public Function Name() As String Implements IShape.Name
                Return "sq"
            End Function
            Public Function Scale(k As Integer) As Double Implements IShape.Scale
                Return _s * k / 2
            End Function
            Public Sub Grow(by As Integer) Implements IShape.Grow
                _s = _s + by
            End Sub
        End Class

        Class Holder
            Public Shape As IShape
        End Class

        Function Describe(s As IShape) As String
            Return s.Name() & ":" & CStr(s.Area())
        End Function

        Sub Main()
            Dim s As IShape = New Sq(3)
            Dim t As String = s.Name()
            Dim a As Integer = s.Area() + 1
            Dim d As Double = s.Scale(3)
            Console.WriteLine(t & " " & CStr(a) & " " & CStr(d))
            s.Grow(1)
            Console.WriteLine(Describe(s))
            Console.WriteLine(s.Area() * 2)
            Dim h As New Holder()
            h.Shape = New Sq(2)
            Dim twice As Integer = h.Shape.Area() * 2
            Console.WriteLine(twice)
        End Sub
        """;

    private const string Expected = "sq 10 4.5\nsq:16\n32\n8";

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
