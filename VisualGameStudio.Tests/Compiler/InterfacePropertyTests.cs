using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A <c>Property</c> declared inside an <c>Interface</c>.
///
/// <para><b>The bug this pins was an infinite loop, not a wrong answer.</b>
/// <c>ParseInterface</c>'s member loop recognised only <c>Function</c> and <c>Sub</c>;
/// anything else fell through to a bare <c>SkipNewlines()</c>, which cannot advance past a
/// non-newline token. So <c>Property</c> spun the parser forever — measured at 11.86s of CPU
/// with output frozen, still running when killed, on every backend.</para>
///
/// <para><b>On the CancelAfter attributes.</b> They bound the tests and document the hazard,
/// but be honest about their limit: NUnit cannot abort a thread spinning in a synchronous
/// loop, so they would NOT rescue a re-introduced hang. The real guarantee is the parser's
/// progress invariant — every iteration of the member loop must consume a token — and that is
/// what <see cref="InterfaceWithAnUnparseableMember_Terminates_WithAnError"/> pins.</para>
/// </summary>
[TestFixture]
public class InterfacePropertyTests
{
    private const string Source =
        "Interface IThing\n" +
        "Property V As Integer\n" +
        "End Interface\n" +
        "Sub Main()\n" +
        "Console.WriteLine(\"ok\")\n" +
        "End Sub";

    [Test]
    [CancelAfter(15000)]
    public void InterfaceProperty_Parses_WithoutHanging()
    {
        var parser = new Parser(new Lexer(Source).Tokenize());
        var ast = parser.Parse();

        Assert.That(parser.Errors, Is.Empty,
            string.Join(" | ", parser.Errors.Select(e => e.ToString())));
        Assert.That(ast.Declarations, Has.Count.EqualTo(2),
            "the interface AND the sibling Sub Main must both survive");
    }

    [Test]
    [CancelAfter(15000)]
    public void InterfaceProperty_ReachesTheAst()
    {
        var ast = new Parser(new Lexer(Source).Tokenize()).Parse();

        var iface = ast.Declarations.OfType<InterfaceNode>().Single();
        var prop = iface.Properties.SingleOrDefault(p => p.Name == "V");

        Assert.That(prop, Is.Not.Null, "InterfaceNode.Properties existed but was never populated");
        Assert.That(prop!.PropertyType?.Name, Is.EqualTo("Integer"));
    }

    /// <summary>
    /// An interface property is inherently bodyless, so it is an auto-property and must not
    /// trip the "must have at least one accessor" rule.
    /// </summary>
    [Test]
    [CancelAfter(15000)]
    public void InterfaceProperty_PassesSemanticAnalysis()
    {
        var ast = new Parser(new Lexer(Source).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();

        Assert.That(analyzer.Analyze(ast), Is.True,
            string.Join(" | ", analyzer.Errors.Select(e => e.Message)));
    }

    [Test]
    [CancelAfter(15000)]
    public void InterfaceProperty_ReachesTheIr()
    {
        var ast = new Parser(new Lexer(Source).Tokenize()).Parse();
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);

        var module = new IRBuilder(analyzer).Build(ast, "T");

        Assert.That(module.Interfaces, Does.ContainKey("IThing"));
        var prop = module.Interfaces["IThing"].Properties.SingleOrDefault(p => p.Name == "V");
        Assert.That(prop, Is.Not.Null,
            "ModuleTypeWalker and JsCapabilityChecker both walk interface properties; " +
            "until now that was unreachable dead code");
    }

    /// <summary>
    /// The durable half of the fix. The hang was not really about properties — it was a parse
    /// loop with no guaranteed progress. ANY unrecognised token must terminate, with an error,
    /// rather than spin. This uses a token the loop still does not handle.
    /// </summary>
    [Test]
    [CancelAfter(15000)]
    public void InterfaceWithAnUnparseableMember_Terminates_WithAnError()
    {
        var parser = new Parser(new Lexer(
            "Interface IThing\n" +
            "Dim NotAllowed As Integer\n" +
            "End Interface\n" +
            "Sub Main()\nEnd Sub").Tokenize());

        parser.Parse();

        Assert.That(parser.Errors, Is.Not.Empty,
            "an unrecognised interface member must be reported, not silently skipped forever");
    }

    /// <summary>Interfaces with only methods must keep working exactly as before.</summary>
    [Test]
    [CancelAfter(15000)]
    public void InterfaceWithMethodsOnly_StillParses()
    {
        var parser = new Parser(new Lexer(
            "Interface IThing\n" +
            "Sub Go()\n" +
            "Function Calc() As Integer\n" +
            "End Interface\n" +
            "Sub Main()\nEnd Sub").Tokenize());
        var ast = parser.Parse();

        Assert.That(parser.Errors, Is.Empty);
        Assert.That(ast.Declarations.OfType<InterfaceNode>().Single().Methods, Has.Count.EqualTo(2));
    }
}
