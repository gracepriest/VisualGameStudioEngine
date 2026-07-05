using System.Text;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.LSP;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// Finding [20]: function/sub/class extent must be the REAL end line (the
/// parser consumes 'End Function'/'End Sub'/'End Class'), not the old
/// "start + statement count + 10" estimate — otherwise locals and parameters
/// disappear from completion in the lower part of longer functions.
/// </summary>
[TestFixture]
public class ScopeExtentTests
{
    private CompletionService _completionService = null!;

    [SetUp]
    public void SetUp()
    {
        _completionService = new CompletionService();
    }

    private static DocumentState CreateParsedState(string sourceCode)
    {
        var uri = DocumentUri.From("file:///test.bas");
        var state = new DocumentState(uri, sourceCode);
        state.Parse();
        return state;
    }

    /// <summary>
    /// Build a function whose top-level statement count is tiny (one If block)
    /// but whose body spans many lines — this defeats the old estimate.
    /// </summary>
    private static string BuildLongFunctionSource(out int cursorLine)
    {
        var sb = new StringBuilder();
        sb.Append("Function Calc(amount As Integer) As Integer\n"); // line 0
        sb.Append("    Dim result As Integer\n");                   // line 1
        sb.Append("    If amount > 0 Then\n");                      // line 2
        for (int i = 0; i < 25; i++)
        {
            sb.Append("        result = result + 1\n");             // lines 3..27
        }
        sb.Append("    End If\n");                                  // line 28
        cursorLine = 29;
        sb.Append("    \n");                                        // line 29 (cursor here)
        sb.Append("    Return result\n");                           // line 30
        sb.Append("End Function\n");                                // line 31
        return sb.ToString();
    }

    [Test]
    public void Parser_RecordsEndLine_OnFunction()
    {
        var source = BuildLongFunctionSource(out _);
        var lexer = new Lexer(source);
        var parser = new Parser(lexer.Tokenize());
        var ast = parser.Parse();

        var func = ast.Declarations.OfType<FunctionNode>().FirstOrDefault(f => f.Name == "Calc");
        Assert.That(func, Is.Not.Null);
        // "End Function" sits on 1-based line 32
        Assert.That(func!.EndLine, Is.EqualTo(32), "parser must record the End Function line");
    }

    [Test]
    public void Parser_RecordsEndLine_OnSubAndClass()
    {
        var source =
            "Class Foo\n" +           // 1-based line 1
            "    Public Sub Bar()\n" + // line 2
            "        PrintLine(1)\n" + // line 3
            "    End Sub\n" +          // line 4
            "End Class\n";             // line 5
        var lexer = new Lexer(source);
        var parser = new Parser(lexer.Tokenize());
        var ast = parser.Parse();

        var cls = ast.Declarations.OfType<ClassNode>().FirstOrDefault(c => c.Name == "Foo");
        Assert.That(cls, Is.Not.Null);
        Assert.That(cls!.EndLine, Is.EqualTo(5), "parser must record the End Class line");

        var sub = cls.Members.OfType<SubroutineNode>().FirstOrDefault(s => s.Name == "Bar");
        Assert.That(sub, Is.Not.Null);
        Assert.That(sub!.EndLine, Is.EqualTo(4), "parser must record the End Sub line");
    }

    [Test]
    public void LocalsAndParameters_SuggestedInLowerPartOfLongFunction()
    {
        var source = BuildLongFunctionSource(out var cursorLine);
        var state = CreateParsedState(source);

        var result = _completionService.GetCompletions(state, cursorLine, 4);

        Assert.That(result.Any(c => c.Label == "amount"), Is.True,
            "parameter 'amount' must be suggested near the end of a long function");
        Assert.That(result.Any(c => c.Label == "result"), Is.True,
            "local 'result' must be suggested near the end of a long function");
    }
}
