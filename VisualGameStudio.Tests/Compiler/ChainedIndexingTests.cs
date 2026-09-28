using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Indexing the result of an index or a call — <c>lst(0)(2)</c>, <c>MakeInts()(1)</c>,
/// <c>lst[0][3]</c> — when the inner value is an ARRAY.
///
/// <para>The paren form's chain arm knew only generic collections, so an array-valued callee
/// fell to the delegate-invocation branch and CALLED the array: <c>t5(2)</c> — CS0149 on C#,
/// "cannot be used as a function" on C++, "t5 is not a function" on JavaScript. The bracket form
/// was folded by the parser into ONE index list (<c>lst[0, 3]</c>), right only for a rank-2
/// array. And on C++ a write through the chain landed in a copy of the List's element.</para>
/// </summary>
[TestFixture]
public class ChainedIndexingTests
{
    private static ArrayAccessExpressionNode Access(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty);
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        Assert.That(analyzer.Errors, Is.Empty, string.Join("; ", analyzer.Errors.Select(e => e.Message)));
        var main = ast.Declarations.OfType<SubroutineNode>().Single(s => s.Name == "Main");
        var print = main.Body.Statements.OfType<ExpressionStatementNode>().Last();
        return (ArrayAccessExpressionNode)((CallExpressionNode)print.Expression).Arguments.Single();
    }

    /// <summary>A rank-1 base takes one index; the rest index its result.</summary>
    [Test]
    public void BracketsOverAListOfArrays_IndexTheElement_ThenTheArray()
    {
        var access = Access("Sub Main()\n    Dim lst As New List(Of Integer[])()\n    Console.WriteLine(lst[0][3])\nEnd Sub");
        Assert.That(access.Indices, Has.Count.EqualTo(1));
        Assert.That(access.Array, Is.TypeOf<ArrayAccessExpressionNode>());
        Assert.That(((ArrayAccessExpressionNode)access.Array).Indices, Has.Count.EqualTo(1));
    }

    /// <summary>C-style chained brackets still index a rank-2 array as one access.</summary>
    [Test]
    public void BracketsOverARankTwoArray_StayOneAccess()
    {
        var access = Access("Sub Main()\n    Dim g[2, 3] As Integer\n    Console.WriteLine(g[1][2])\nEnd Sub");
        Assert.That(access.Indices, Has.Count.EqualTo(2));
        Assert.That(access.Array, Is.TypeOf<IdentifierExpressionNode>());
    }
}

[TestFixture]
[Category("Integration")]   // spawns node, a C++ compiler and dotnet
[NonParallelizable]
public class ChainedIndexingExecutionTests
{
    private const string Program = """
        Function MakeInts() As Integer()
            Return {10, 20, 30}
        End Function

        Function MakeGrid() As Integer(,)
            Dim g(1, 1) As Integer
            g(1, 1) = 7
            Return g
        End Function

        Sub Main()
            Dim a[] As Integer = {1, 2, 3, 4}
            Dim lst As New List(Of Integer[])()
            lst.Add(a)
            Console.WriteLine(lst(0)(2))
            Console.WriteLine(lst[0][3])
            Console.WriteLine(MakeInts()(1))
            Console.WriteLine(MakeInts()[2])
            Console.WriteLine(MakeGrid()(1, 1))
            lst(0)(2) = 9
            lst[0][0] = 5
            lst(0)(1) += 5
            Console.WriteLine(CStr(lst(0)(0)) & CStr(lst(0)(1)) & CStr(lst(0)(2)))
            Dim t As Integer = 0
            For i As Integer = 0 To 3
                t = t + lst(0)(i)
            Next
            Console.WriteLine(t)
            Dim ls As New List(Of Integer)()
            ls.Add(42)
            Dim arr[] As List(Of Integer) = {ls}
            Console.WriteLine(arr(0)(0) + arr[0][0])
            Dim g[2, 3] As Integer
            g[1][2] = 6
            Console.WriteLine(g(1, 2))
        End Sub
        """;

    private const string Expected = "3\n4\n20\n30\n7\n579\n25\n84\n6";

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
