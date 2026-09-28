using System.Collections.Generic;
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
/// A class's own <c>Operator</c> (#198). Three defects, one feature:
/// <list type="number">
/// <item><c>Operator =(…)</c> did not parse — "Expected operator symbol after 'Operator'": the
/// declaration parser checked for the <c>Equal</c> token, and <c>=</c> lexes as
/// <c>Assignment</c>.</item>
/// <item>A declared operator was never CALLED. <c>Box + Box</c> was refused as non-numeric, and
/// <c>Box = Box</c> fell through to a REFERENCE comparison — on C++ silently: two value-equal
/// boxes printed False and the <c>If x = y</c> branch never ran. (C# only looked right because
/// csc resolves <c>==</c> to the emitted <c>operator ==</c> itself.)</item>
/// <item>Nothing checked a declaration, so an unpaired <c>=</c> or an <c>Operator ^</c> reached
/// csc as CS0216 / a non-compiling <c>operator Exponent</c>.</item>
/// </list>
/// The analyzer now binds <c>a op b</c> to the class's operator (base classes included); the IR
/// is a call to the static <c>op_*</c> function, which C++ calls by name and C# renders back as
/// the infix operator. JavaScript still refuses user operators (BL7006).
/// </summary>
[TestFixture]
public class UserOperatorTests
{
    private const string Box = """
        Class Box
            Public V As Integer
            Public Sub New(v As Integer)
                Me.V = v
            End Sub
            Public Shared Operator =(a As Box, b As Box) As Boolean
                Return a.V = b.V
            End Operator
            Public Shared Operator <>(a As Box, b As Box) As Boolean
                Return a.V <> b.V
            End Operator
            Public Shared Operator +(a As Box, b As Box) As Box
                Return New Box(a.V + b.V)
            End Operator
        End Class

        """;

    private static (ProgramNode Ast, List<string> Errors) Analyze(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        if (parser.Errors.Count > 0) return (ast, parser.Errors.Select(e => e.Message).ToList());
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return (ast, analyzer.Errors.Select(e => e.Message).ToList());
    }

    private static BinaryExpressionNode LastBinary(ProgramNode ast)
    {
        var main = ast.Declarations.OfType<SubroutineNode>().Single(s => s.Name == "Main");
        var decl = main.Body.Statements.OfType<VariableDeclarationNode>().Last();
        return (BinaryExpressionNode)decl.Initializer;
    }

    [Test]
    public void OperatorEquals_Parses()
    {
        var parser = new Parser(new Lexer(Box + "Sub Main()\nEnd Sub").Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty);
        var symbols = ast.Declarations.OfType<ClassNode>().Single().Members
            .OfType<OperatorDeclarationNode>().Select(o => o.OperatorSymbol);
        Assert.That(symbols, Is.EqualTo(new[] { "=", "<>", "+" }));
    }

    [TestCase("Dim t As Boolean = x = y", "=", "Boolean")]
    [TestCase("Dim t As Boolean = x <> y", "<>", "Boolean")]
    [TestCase("Dim t As Box = x + y", "+", "Box")]
    public void AUse_BindsToTheClassOperator(string body, string symbol, string type)
    {
        var (ast, errors) = Analyze(Box + $"Sub Main()\n    Dim x As New Box(1)\n    Dim y As New Box(1)\n    {body}\nEnd Sub");
        Assert.That(errors, Is.Empty);
        var bin = LastBinary(ast);
        Assert.That(bin.UserOperatorClass, Is.EqualTo("Box"));
        Assert.That(bin.UserOperatorSymbol, Is.EqualTo(symbol));
    }

    /// <summary>`Is` stays reference identity (ADR-0011 D5) — a user `Operator =` never answers it.</summary>
    [Test]
    public void Is_IsNotBoundToTheUserOperator()
    {
        var (ast, errors) = Analyze(Box + "Sub Main()\n    Dim x As New Box(1)\n    Dim y As New Box(1)\n    Dim t As Boolean = x Is y\nEnd Sub");
        Assert.That(errors, Is.Empty);
        Assert.That(LastBinary(ast).UserOperatorClass, Is.Null);
    }

    /// <summary>A base class's operator answers for a derived operand, declared in either order.</summary>
    [Test]
    public void ABaseClassOperator_BindsForADerivedOperand_DeclaredLater()
    {
        var (ast, errors) = Analyze(
            "Sub Main()\n    Dim a As New Tip(1)\n    Dim b As New Tip(1)\n    Dim t As Boolean = a = b\nEnd Sub\n" +
            Box + "Class Tip\n    Inherits Box\n    Public Sub New(v As Integer)\n        MyBase.New(v)\n    End Sub\nEnd Class");
        Assert.That(errors, Is.Empty);
        Assert.That(LastBinary(ast).UserOperatorClass, Is.EqualTo("Box"));
    }

    [TestCase("Public Shared Operator =(a As Box, b As Box) As Boolean\nReturn True\nEnd Operator",
        "'Operator =' requires a matching 'Operator <>' in class 'Box'")]
    [TestCase("Public Shared Operator <(a As Box, b As Box) As Boolean\nReturn True\nEnd Operator",
        "'Operator <' requires a matching 'Operator >' in class 'Box'")]
    [TestCase("Public Shared Operator ^(a As Box, b As Box) As Box\nReturn a\nEnd Operator",
        "'Operator ^' is not supported yet")]
    [TestCase("Public Shared Operator -(a As Box) As Box\nReturn a\nEnd Operator",
        "'Operator -' must take exactly two parameters")]
    [TestCase("Public Shared Operator +(a As Integer, b As Integer) As Integer\nReturn a\nEnd Operator",
        "At least one parameter of 'Operator +' must be of the containing type 'Box'")]
    public void ABadDeclaration_IsRefusedWithItsFix(string member, string error)
    {
        var (_, errors) = Analyze($"Class Box\n{member}\nEnd Class\nSub Main()\nEnd Sub");
        Assert.That(errors, Has.Some.Contains(error));
    }

    /// <summary>The C++ output calls the operator; before, it compared the two shared_ptrs.</summary>
    [Test]
    public void Cpp_CallsTheOperator()
    {
        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(InterpolatedStringLoweringTests.Optimized(
                Box + "Sub Main()\n    Dim x As New Box(1)\n    Dim y As New Box(1)\n    Console.WriteLine(x = y)\nEnd Sub"));
        Assert.That(cpp, Does.Contain("Box::op_Equality(x, y)"));
    }

    /// <summary>JavaScript has no operator overloading; BL7006 still refuses the class.</summary>
    [Test]
    public void JavaScript_StillRefusesUserOperators()
    {
        var module = JsTestSupport.BuildModule(Box + "Sub Main()\n    Dim x As New Box(1)\n    Console.WriteLine(x = x)\nEnd Sub");
        var ex = Assert.Throws<ForeignFeatureException>(() => new JavaScriptCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("BL7006"));
    }
}

[TestFixture]
[Category("Integration")]   // spawns a C++ compiler and dotnet (JavaScript refuses user operators: BL7006)
[NonParallelizable]
public class UserOperatorExecutionTests
{
    private const string Program = """
        Sub Main()
            Dim a As New Money(5)
            Dim b As New Money(5)
            Dim c As New Money(9)
            Console.WriteLine(a = b)
            Console.WriteLine(a Is b)
            Console.WriteLine(a <> c)
            Console.WriteLine(a < c)
            Console.WriteLine(c > a)
            Console.WriteLine((a * 3).Cents)
            Console.WriteLine((c Mod a).Cents)
            Console.WriteLine((a And c).Cents)
            If a = b AndAlso Not a = c Then Console.WriteLine("and-also")
            Dim t As Boolean = a = b
            Console.WriteLine(t)
            Dim d1 As New Tip(5)
            Dim d2 As New Tip(5)
            Console.WriteLine(d1 = d2)
            Console.WriteLine(a.Both(b, c))
            Dim total As Money = a + b + c
            Console.WriteLine(total.Cents)
        End Sub

        Class Money
            Public Cents As Integer
            Public Sub New(c As Integer)
                Cents = c
            End Sub
            Public Function Both(p As Money, q As Money) As Boolean
                Return p = q
            End Function
            Public Shared Operator =(x As Money, y As Money) As Boolean
                Return x.Cents = y.Cents
            End Operator
            Public Shared Operator <>(x As Money, y As Money) As Boolean
                Return Not x = y
            End Operator
            Public Shared Operator <(x As Money, y As Money) As Boolean
                Return x.Cents < y.Cents
            End Operator
            Public Shared Operator >(x As Money, y As Money) As Boolean
                Return x.Cents > y.Cents
            End Operator
            Public Shared Operator +(x As Money, y As Money) As Money
                Return New Money(x.Cents + y.Cents)
            End Operator
            Public Shared Operator *(x As Money, k As Integer) As Money
                Return New Money(x.Cents * k)
            End Operator
            Public Shared Operator Mod(x As Money, y As Money) As Money
                Return New Money(x.Cents Mod y.Cents)
            End Operator
            Public Shared Operator And(x As Money, y As Money) As Money
                Return New Money(x.Cents + y.Cents + 1000)
            End Operator
        End Class

        Class Tip
            Inherits Money
            Public Sub New(c As Integer)
                MyBase.New(c)
            End Sub
        End Class
        """;

    /// <summary>
    /// `a = b` True while `a Is b` False: the user operator answers value equality and never
    /// identity — ADR-0011 D4 (1)'s invariant, which waited on a user `Operator =` (#198).
    /// </summary>
    private const string Expected =
        "True\nFalse\nTrue\nTrue\nTrue\n15\n4\n1014\nand-also\nTrue\nTrue\nFalse\n19";

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd('\n');

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
