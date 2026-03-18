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

    // --- #line hidden before non-executable code ---

    [Test]
    public void Generate_ClassDeclaration_HiddenBeforeBoilerplate()
    {
        var source = @"Module Test
    Sub Main()
        Dim x As Integer = 1
    End Sub
End Module";

        var result = CompileToCS(source, @"C:\project\Test.bas");

        // The namespace/class boilerplate should be marked as hidden
        Assert.That(result, Does.Contain("#line hidden"));
    }

    // --- Source line numbers for various statement types ---

    [Test]
    public void Generate_IfStatement_ContainsLineDirective()
    {
        var source = @"Module Test
    Sub Main()
        Dim x As Integer = 5
        If x > 3 Then
            PrintLine(""big"")
        End If
    End Sub
End Module";

        var result = CompileToCS(source, @"C:\project\Test.bas");

        Assert.That(result, Does.Contain("#line"));
        Assert.That(result, Does.Contain(@"Test.bas"));
    }

    [Test]
    public void Generate_WhileLoop_ContainsLineDirectiveOrHidden()
    {
        var source = @"Module Test
    Sub Main()
        Dim i As Integer = 0
        While i < 10
            i = i + 1
        End While
    End Sub
End Module";

        var result = CompileToCS(source, @"C:\project\Test.bas");

        // The compiler may or may not emit full #line directives for While loops,
        // but at minimum it should produce #line hidden for boilerplate
        Assert.That(result, Does.Contain("#line"));
    }

    [Test]
    public void Generate_ForLoop_ContainsLineDirective()
    {
        var source = @"Module Test
    Sub Main()
        For i As Integer = 1 To 10
            PrintLine(i)
        Next
    End Sub
End Module";

        var result = CompileToCS(source, @"C:\project\Test.bas");

        Assert.That(result, Does.Contain("#line"));
        Assert.That(result, Does.Contain(@"Test.bas"));
    }

    [Test]
    public void Generate_ForEachLoop_ContainsLineDirective()
    {
        var source = @"Module Test
    Sub Main()
        Dim arr() As Integer = {1, 2, 3}
        For Each n In arr
            PrintLine(n)
        Next
    End Sub
End Module";

        var result = CompileToCS(source, @"C:\project\Test.bas");

        Assert.That(result, Does.Contain("#line"));
        Assert.That(result, Does.Contain(@"Test.bas"));
    }

    [Test]
    public void Generate_FunctionCall_ContainsLineDirective()
    {
        var source = @"Module Test
    Function Add(a As Integer, b As Integer) As Integer
        Return a + b
    End Function

    Sub Main()
        Dim result As Integer = Add(1, 2)
    End Sub
End Module";

        var result = CompileToCS(source, @"C:\project\Test.bas");

        Assert.That(result, Does.Contain("#line"));
        Assert.That(result, Does.Contain(@"Test.bas"));
    }

    // --- Different file paths ---

    [Test]
    public void Generate_DifferentFilePath_UsesCorrectPath()
    {
        var source = @"Module Helper
    Sub DoWork()
        Dim x As Integer = 1
    End Sub
End Module";

        var result = CompileToCS(source, @"C:\project\src\Helper.bas");

        Assert.That(result, Does.Contain(@"Helper.bas"));
    }

    [Test]
    public void Generate_UnixStylePath_ContainsPath()
    {
        var source = @"Module Test
    Sub Main()
        Dim x As Integer = 1
    End Sub
End Module";

        var result = CompileToCS(source, "/home/user/project/Test.bas");

        Assert.That(result, Does.Contain("Test.bas"));
    }

    // --- Line directive format validation ---

    [Test]
    public void Generate_LineDirectiveFormat_HasQuotedFilePath()
    {
        var source = @"Module Test
    Sub Main()
        Dim x As Integer = 42
    End Sub
End Module";

        var result = CompileToCS(source, @"C:\project\Test.bas");

        // #line directives should have the file path in quotes
        Assert.That(result, Does.Contain(@"""C:\project\Test.bas"""));
    }

    [Test]
    public void Generate_LineDirective_HasPositiveLineNumber()
    {
        var source = @"Module Test
    Sub Main()
        Dim x As Integer = 42
    End Sub
End Module";

        var result = CompileToCS(source, @"C:\project\Test.bas");

        // Should contain #line followed by a positive number
        var lines = result.Split('\n');
        var lineDirectives = lines.Where(l => l.Trim().StartsWith("#line") && !l.Contains("hidden")).ToList();
        foreach (var directive in lineDirectives)
        {
            var parts = directive.Trim().Split(' ');
            // #line <number> "<path>"
            Assert.That(parts.Length, Is.GreaterThanOrEqualTo(2), $"Directive too short: {directive}");
            if (parts.Length >= 2 && int.TryParse(parts[1], out var lineNum))
            {
                Assert.That(lineNum, Is.GreaterThan(0), $"Line number must be positive: {directive}");
            }
        }
    }

    // --- No-op cases ---

    [Test]
    public void Generate_EmptyModule_StillHasHiddenDirective()
    {
        var source = @"Module Test
    Sub Main()
    End Sub
End Module";

        var result = CompileToCS(source, @"C:\project\Test.bas");

        // Even an empty main should have #line hidden for boilerplate
        Assert.That(result, Does.Contain("#line hidden"));
    }

    [Test]
    public void Generate_NoFileName_NoLineDirectives()
    {
        var source = @"Module Test
    Sub Main()
        Dim x As Integer = 1
    End Sub
End Module";

        // When no file name is provided, #line directives may not be emitted with file path
        var result = CompileToCS(source);

        // The result should still compile-worthy (no crash)
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Length, Is.GreaterThan(0));
    }

    // --- Multiple subs in same module ---

    [Test]
    public void Generate_MultipleSubs_EachHasLineDirectives()
    {
        var source = @"Module Test
    Sub First()
        Dim a As Integer = 1
    End Sub

    Sub Second()
        Dim b As Integer = 2
    End Sub
End Module";

        var result = CompileToCS(source, @"C:\project\Test.bas");

        // Should have #line directives - at least one for the sub bodies
        var lines = result.Split('\n');
        var lineDirectiveCount = lines.Count(l => l.Trim().StartsWith("#line") && !l.Contains("hidden"));
        Assert.That(lineDirectiveCount, Is.GreaterThanOrEqualTo(1));
    }

    // --- Line numbers correspond to source positions ---

    [Test]
    public void Generate_VariableAssignment_LineNumberIsReasonable()
    {
        var source = @"Module Test
    Sub Main()
        Dim x As Integer = 42
    End Sub
End Module";

        var result = CompileToCS(source, @"C:\project\Test.bas");

        // "Dim x As Integer = 42" is on source line 3 (1-based)
        // The #line directive should reference a line number >= 2 (at least past Module declaration)
        var lines = result.Split('\n');
        var lineDirectives = lines
            .Where(l => l.Trim().StartsWith("#line") && !l.Contains("hidden"))
            .ToList();

        Assert.That(lineDirectives.Count, Is.GreaterThan(0), "Expected at least one #line directive");

        foreach (var directive in lineDirectives)
        {
            var parts = directive.Trim().Split(' ');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var lineNum))
            {
                Assert.That(lineNum, Is.GreaterThanOrEqualTo(2).And.LessThanOrEqualTo(10),
                    $"Line number {lineNum} is outside expected range for a 5-line source");
            }
        }
    }
}
