using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.JavaScript;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The two array declaration spellings mean different things:
/// <list type="bullet">
/// <item><c>Dim a[n]</c>, BasicLang's preferred form: n ELEMENTS, C-style.</item>
/// <item><c>Dim a(n)</c>, kept so older BASIC programs run: UPPER BOUND n, as in VB, so n + 1
/// elements.</item>
/// </list>
/// Both used to mean n elements, which contradicted language.md (<c>Dim nums(9) ' 10
/// elements</c>): C# threw IndexOutOfRangeException on <c>a(9) = x</c> and C++ wrote past the
/// end. Once declared, both are indexed the same way, including with a comma list inside
/// brackets (<c>grid[x, y]</c>), which used to fail to parse.
/// </summary>
[TestFixture]
public class ArrayBoundsTests
{
    private static TypeReference DeclaredType(string declaration)
    {
        var parser = new Parser(new Lexer(declaration).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty);
        return ast.Declarations.OfType<VariableDeclarationNode>().Single().Type;
    }

    private static object LiteralSize(TypeReference type, int dimension) =>
        ((LiteralExpressionNode)type.ArrayDimensions[dimension]).Value;

    [Test]
    public void Brackets_DeclareAnElementCount()
    {
        Assert.That(LiteralSize(DeclaredType("Dim a[2] As Integer"), 0), Is.EqualTo(2));
        var grid = DeclaredType("Dim g[3, 4] As Integer");
        Assert.That(LiteralSize(grid, 0), Is.EqualTo(3));
        Assert.That(LiteralSize(grid, 1), Is.EqualTo(4));
    }

    [Test]
    public void Parentheses_DeclareAnUpperBound_ALiteralIsFolded()
    {
        Assert.That(LiteralSize(DeclaredType("Dim a(2) As Integer"), 0), Is.EqualTo(3));
        var grid = DeclaredType("Dim g(2, 3) As Integer");
        Assert.That(LiteralSize(grid, 0), Is.EqualTo(3));
        Assert.That(LiteralSize(grid, 1), Is.EqualTo(4));
    }

    [Test]
    public void Parentheses_WithANonLiteralBound_BecomeBoundPlusOne()
    {
        var size = DeclaredType("Dim a(N) As Integer").ArrayDimensions[0];

        Assert.That(size, Is.InstanceOf<BinaryExpressionNode>());
        var plus = (BinaryExpressionNode)size;
        Assert.That(plus.Operator, Is.EqualTo("+"));
        Assert.That(((IdentifierExpressionNode)plus.Left).Name, Is.EqualTo("N"));
        Assert.That(((LiteralExpressionNode)plus.Right).Value, Is.EqualTo(1));
    }

    [Test]
    public void AnUnsizedArray_StaysUnsized()
        => Assert.That(DeclaredType("Dim a() As Integer").ArrayDimensions, Is.EqualTo(new ExpressionNode?[] { null }));

    [Test]
    public void ACommaListInsideBrackets_IndexesEveryDimension()
    {
        var parser = new Parser(new Lexer("Sub Main()\nDim g[3, 4] As Integer\ng[2, 3] = 5\nEnd Sub").Tokenize());
        parser.Parse();
        Assert.That(parser.Errors, Is.Empty);
    }

    [Test]
    public void TheTwoSpellings_ReachTheBackendsWithTheirOwnSizes()
    {
        const string source = "Sub Main()\nDim a(2) As Integer\nDim b[2] As Integer\nEnd Sub";
        var cs = new BasicLang.Compiler.CodeGen.CSharp.CSharpCodeGenerator().Generate(JsTestSupport.BuildModule(source));

        Assert.That(cs, Does.Contain("int[] a = new int[3];"), cs);
        Assert.That(cs, Does.Contain("int[] b = new int[2];"), cs);
    }
}

[TestFixture]
[Category("Integration")]   // spawns node, a C++ compiler and dotnet
[NonParallelizable]
public class ArrayBoundsExecutionTests
{
    /// <summary>Every valid index of both spellings is written and read; .NET prints 36 and 3 2.</summary>
    private const string Program = @"
Sub Main()
    Dim grid[3, 4] As Integer
    Dim old(2, 3) As Integer
    Dim s As Integer = 0
    For x As Integer = 0 To 2
        For y As Integer = 0 To 3
            grid[x, y] = 1
            old(x, y) = 2
        Next
    Next
    For x As Integer = 0 To 2
        For y As Integer = 0 To 3
            s = s + grid(x, y) + old[x, y]
        Next
    Next
    Console.WriteLine(s)
    Dim a(2) As Integer
    Dim b[2] As Integer
    a(2) = 7
    Console.WriteLine(a.Length)
    Console.WriteLine(b.Length)
End Sub";

    private const string Expected = "36\n3\n2";

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
