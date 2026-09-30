using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>TypeOf x Is T</c> (#197). The lexer had a <c>TypeOf</c> token and nothing parsed it —
/// "Unexpected token in expression: 'TypeOf'" wherever it was written. The parser now desugars it
/// to <c>TryCast(x, T) IsNot Nothing</c> (<c>Is Nothing</c> for <c>IsNot T</c>), built from two
/// forms every backend already lowers; the cast is flagged so the analyzer judges it by TypeOf's
/// rules. JavaScript refuses an interface target (BL7013): its TryCast to an interface passes the
/// value through, which would make every object match.
/// </summary>
[TestFixture]
public class TypeOfTests
{
    private const string Animals = """
        Class Animal
        End Class
        Class Dog
            Inherits Animal
        End Class
        Class Cat
            Inherits Animal
        End Class
        Interface IShape
        End Interface

        """;

    private static (ProgramNode Ast, string Errors) Analyze(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        if (parser.Errors.Count > 0) return (ast, string.Join("; ", parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return (ast, string.Join("; ", analyzer.Errors.Select(e => e.Message)));
    }

    private static string InMain(string body) =>
        Animals + "Sub Main()\n    Dim a As Animal = New Dog()\n    Dim t As Boolean\n    " + body + "\nEnd Sub";

    [TestCase("If TypeOf a Is Dog Then t = True")]
    [TestCase("t = TypeOf a IsNot Cat")]
    [TestCase("t = Not TypeOf a Is Dog")]
    [TestCase("Console.WriteLine(TypeOf a Is Animal)")]
    [TestCase("t = TypeOf a Is Dog AndAlso a IsNot Nothing")]
    [TestCase("t = TypeOf a Is IShape")]
    [TestCase("Dim s As IShape\n    t = TypeOf s Is Dog")]
    public void EveryShape_Analyzes(string body)
        => Assert.That(Analyze(InMain(body)).Errors, Is.Empty);

    /// <summary>The test binds the whole `TypeOf … Is T` — `Not` negates it (VB precedence, #195).</summary>
    [Test]
    public void TypeOf_Desugars_ToAFlaggedTryCastAgainstNothing()
    {
        var (ast, errors) = Analyze(InMain("Dim u As Boolean = TypeOf a IsNot Dog"));
        Assert.That(errors, Is.Empty);
        var main = ast.Declarations.OfType<SubroutineNode>().Single(s => s.Name == "Main");
        var test = (BinaryExpressionNode)main.Body.Statements.OfType<VariableDeclarationNode>().Last().Initializer;
        Assert.That(test.Operator, Is.EqualTo("Is"), "TypeOf … IsNot T is TryCast(…) Is Nothing");
        var cast = (CastExpressionNode)test.Left;
        Assert.That(cast.IsTryCast && cast.IsTypeOfTest, Is.True);
        Assert.That(cast.TargetType.Name, Is.EqualTo("Dog"));
    }

    /// <summary>A class declared BELOW the test is judged on its declared ancestry, not refused.</summary>
    [Test]
    public void ClassesDeclaredLater_AreRelatedByTheirInherits()
        => Assert.That(Analyze("Sub Main()\n    Dim a As Animal\n    Console.WriteLine(TypeOf a Is Dog)\nEnd Sub\n" + Animals).Errors,
            Is.Empty);

    [TestCase("Dim d As Dog = New Dog()\n    t = TypeOf d Is Cat", "Expression of type 'Dog' can never be of type 'Cat'")]
    [TestCase("t = TypeOf a Is Integer", "'TypeOf … Is Integer' is not supported: the type must be a class or an interface")]
    [TestCase("Dim n As Integer = 1\n    t = TypeOf n Is Dog", "'TypeOf' needs a reference, but the expression has type 'Integer'")]
    [TestCase("t = TypeOf a Dog", "Expected 'Is' or 'IsNot' after 'TypeOf' expression")]
    public void AMisuse_IsRefusedWithItsReason(string body, string error)
        => Assert.That(Analyze(InMain(body)).Errors, Does.Contain(error));

    [Test]
    public void JavaScript_RefusesAnInterfaceTarget()
    {
        var module = JsTestSupport.BuildModule(InMain("Console.WriteLine(TypeOf a Is IShape)"));
        var ex = Assert.Throws<ForeignFeatureException>(() => new JavaScriptCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("BL7013"));
    }
}

[TestFixture]
[Category("Integration")]   // spawns node, a C++ compiler and dotnet
[NonParallelizable]
public class TypeOfExecutionTests
{
    private const string Program = """
        Class Holder
            Public Pet As Animal
        End Class
        Class Animal
            Public Legs As Integer = 4
        End Class
        Class Dog
            Inherits Animal
        End Class
        Class Cat
            Inherits Animal
        End Class

        Function Make(k As Integer) As Animal
            If k = 1 Then Return New Dog()
            Return New Cat()
        End Function

        Sub Main()
            Dim a As Animal = New Dog()
            Dim n As Animal
            If TypeOf a Is Dog Then Console.WriteLine("dog")
            If TypeOf a Is Cat Then Console.WriteLine("cat") Else Console.WriteLine("not cat")
            Console.WriteLine(TypeOf a Is Animal)
            Console.WriteLine(TypeOf n Is Animal)
            Console.WriteLine(TypeOf a IsNot Cat)
            Console.WriteLine(Not TypeOf a Is Dog)
            Dim h As New Holder()
            h.Pet = New Cat()
            Console.WriteLine(TypeOf h.Pet Is Cat)
            Console.WriteLine(TypeOf Make(1) Is Dog)
            Console.WriteLine(TypeOf Make(2) Is Dog)
            If TypeOf a Is Dog AndAlso a.Legs = 4 Then Console.WriteLine("four-legged dog")
            Dim b As Boolean = TypeOf a IsNot Dog
            Console.WriteLine(b)
        End Sub
        """;

    private const string Expected =
        "dog\nnot cat\nTrue\nFalse\nTrue\nFalse\nTrue\nTrue\nFalse\nfour-legged dog\nFalse";

    /// <summary>Interface targets and operands — C# and C++ (JavaScript refuses, BL7013).</summary>
    private const string Interfaces = """
        Interface IShape
            Function Area() As Integer
        End Interface
        Class Base
        End Class
        Class Sq
            Inherits Base
            Implements IShape
            Public Function Area() As Integer Implements IShape.Area
                Return 4
            End Function
        End Class
        Class Blob
            Inherits Base
        End Class
        Sub Main()
            Dim s As IShape = New Sq()
            Dim x As Base = New Sq()
            Dim y As Base = New Blob()
            Console.WriteLine(TypeOf s Is Sq)
            Console.WriteLine(TypeOf x Is IShape)
            Console.WriteLine(TypeOf y Is IShape)
        End Sub
        """;

    private const string InterfacesExpected = "True\nTrue\nFalse";

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd('\n');

    private static string RunCpp(string source)
    {
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(InterpolatedStringLoweringTests.Optimized(source));
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
    public void Interfaces_OnCpp() => Assert.That(RunCpp(Interfaces), Is.EqualTo(InterfacesExpected));

    [Test]
    public void Interfaces_OnCSharp() =>
        Assert.That(Normalize(CliTestHarness.CompileRunCSharp(Interfaces)), Is.EqualTo(InterfacesExpected));
}
