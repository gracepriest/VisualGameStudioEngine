using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class LineDirectiveTests
{
    private string CompileToCS(string basSource, string fileName = "Test.bas")
    {
        var lexer = new Lexer(basSource);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        var irBuilder = new IRBuilder(analyzer);
        var module = irBuilder.Build(ast, "Test", fileName);
        var generator = new ImprovedCSharpCodeGenerator();
        return generator.Generate(module);
    }

    [Test]
    public void Generate_SimpleSubMain_ContainsLineDirective()
    {
        var source = @"Module Test
    Sub Main()
        Dim x As Integer = 42
        PrintLine(x)
    End Sub
End Module";

        var result = CompileToCS(source, @"C:\project\Test.bas");

        Assert.That(result, Does.Contain("#line"));
        Assert.That(result, Does.Contain("Test.bas"));
    }

    [Test]
    public void Generate_UsingStatements_MarkedAsHidden()
    {
        var source = @"Module Test
    Sub Main()
        PrintLine(""hello"")
    End Sub
End Module";

        var result = CompileToCS(source, @"C:\project\Test.bas");

        Assert.That(result, Does.Contain("#line hidden"));
    }

    [Test]
    public void Generate_MultipleLines_CorrectLineNumbers()
    {
        var source = @"Module Test
    Sub Main()
        Dim x As Integer = 1
        Dim y As Integer = 2
        Dim z As Integer = x + y
    End Sub
End Module";

        var result = CompileToCS(source, @"C:\project\Test.bas");

        Assert.That(result, Does.Contain("#line"));
        Assert.That(result, Does.Contain(@"Test.bas"));
    }
}
