using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Auto-properties: <c>Public Property V As Integer</c> with no Get/Set body.
///
/// <para>Before this, <c>ParseProperty</c> ended with an unconditional
/// <c>Consume(TokenType.EndProperty)</c>, so a bodyless property threw, <c>Parse()</c>
/// recorded the error and <c>Synchronize()</c> skipped to EOF — discarding the rest of the
/// FILE, including unrelated top-level declarations. The CLI reported it as a parse error
/// (so this was never a miscompile), but the language could not express the most common
/// property form, and the repo's own ParserErrorTests already treated that syntax as valid.</para>
///
/// <para>The desugaring lives in IRBuilder, not in either backend: a synthesized backing
/// field plus real getter/setter IRFunctions means the C#, C++ and JavaScript backends all
/// keep their existing property paths unchanged. CLAUDE.md: change it once, not
/// per-consumer.</para>
/// </summary>
[TestFixture]
public class AutoPropertyTests
{
    private const string Source =
        "Class C\n" +
        "Public Property V As Integer\n" +
        "End Class\n" +
        "Sub Main()\n" +
        "Console.WriteLine(\"x\")\n" +
        "End Sub";

    private static ProgramNode Parse(string src, out Parser parser)
    {
        parser = new Parser(new Lexer(src).Tokenize());
        return parser.Parse();
    }

    [Test]
    public void AutoProperty_ParsesWithoutError()
    {
        var ast = Parse(Source, out var parser);

        Assert.That(parser.Errors, Is.Empty,
            "a bodyless property must parse: " +
            string.Join(" | ", parser.Errors.Select(e => e.ToString())));
        Assert.That(ast.Declarations, Has.Count.EqualTo(2),
            "the class AND the sibling Sub Main must both survive — a parse failure here " +
            "used to Synchronize() to EOF and silently discard the rest of the file");
    }

    [Test]
    public void AutoProperty_ReachesTheIrAsAProperty()
    {
        var ast = Parse(Source, out _);
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            string.Join(" | ", analyzer.Errors.Select(e => e.Message)));

        var module = new IRBuilder(analyzer).Build(ast, "T");

        Assert.That(module.Classes, Does.ContainKey("C"));
        var prop = module.Classes["C"].Properties.SingleOrDefault(p => p.Name == "V");
        Assert.That(prop, Is.Not.Null, "the property must reach the IR");
        Assert.That(prop!.Type?.Name, Is.EqualTo("Integer"));
        Assert.That(module.Functions.Any(f => f.Name == "Main"), Is.True,
            "the sibling Sub Main must not be lost");
    }

    /// <summary>
    /// An auto-property reaches the IR with NULL accessors — that is what marks it as
    /// auto. Each backend renders it with its own idiom rather than IRBuilder synthesizing
    /// accessor bodies: C# has real auto-property syntax, and C++ needs a data member
    /// anyway because member access lowers to <c>IRFieldAccess</c> by name
    /// (<c>IRBuilder.cs:3348</c>), not to a <c>get_</c> call.
    /// </summary>
    [Test]
    public void AutoProperty_ReachesTheIrWithNullAccessors()
    {
        var ast = Parse(Source, out _);
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        var module = new IRBuilder(analyzer).Build(ast, "T");

        var prop = module.Classes["C"].Properties.Single(p => p.Name == "V");

        Assert.That(prop.Getter, Is.Null);
        Assert.That(prop.Setter, Is.Null);
    }

    /// <summary>
    /// Without this the C# backend emits `public int V { }` — a property with no accessors,
    /// which does not compile. `{ get; set; }` is the idiomatic rendering and lets the C#
    /// compiler generate the backing field.
    /// </summary>
    [Test]
    public void AutoProperty_EmitsCompilableCSharp()
    {
        var ast = Parse(Source, out _);
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        var module = new IRBuilder(analyzer).Build(ast, "T");

        var cs = new CSharpCodeGenerator().Generate(module);

        Assert.That(cs, Does.Contain("int V"), "the property must be emitted");
        Assert.That(cs, Does.Match(@"int V\s*\{\s*get;\s*set;\s*\}"),
            "an accessorless property body is not valid C#");
    }

    /// <summary>
    /// C++ needs an actual data member: a read of <c>obj.V</c> lowers to IRFieldAccess by
    /// NAME, so emitting only <c>get_V()</c>/<c>set_V()</c> would leave every call site
    /// referring to a member that does not exist. The accessors are emitted too, because an
    /// interface property declares them as pure virtuals.
    /// </summary>
    [Test]
    public void AutoProperty_EmitsCppMemberAndAccessors()
    {
        var ast = Parse(Source, out _);
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        var module = new IRBuilder(analyzer).Build(ast, "T");

        var cpp = new BasicLang.Compiler.CodeGen.CPlusPlus.CppCodeGenerator().Generate(module);

        Assert.That(cpp, Does.Match(@"int32_t\s+V\s*(=|;)"), "needs a real data member named V");
        Assert.That(cpp, Does.Contain("get_V()"), "interface properties declare get_X as a pure virtual");
        Assert.That(cpp, Does.Contain("set_V("));
    }

    /// <summary>
    /// ReadOnly and WriteOnly auto-properties. C# has an auto form for get-only
    /// (<c>{ get; }</c>) but NOT for set-only — <c>{ set; }</c> is CS8051, "auto-implemented
    /// properties must have get accessors" — so WriteOnly must fall back to an explicit
    /// backing field rather than silently emitting a file that will not compile.
    /// </summary>
    [TestCase("ReadOnly", @"int V\s*\{\s*get;\s*\}", TestName = "ReadOnly_UsesAutoGetOnly")]
    [TestCase("WriteOnly", @"int V\s*\{\s*set\s*\{", TestName = "WriteOnly_FallsBackToExplicitBackingField")]
    public void AutoProperty_RespectsAccessModifiers(string modifier, string expected)
    {
        var src =
            $"Class C\nPublic {modifier} Property V As Integer\nEnd Class\nSub Main()\nEnd Sub";

        var ast = Parse(src, out var parser);
        Assert.That(parser.Errors, Is.Empty,
            string.Join(" | ", parser.Errors.Select(e => e.ToString())));

        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            string.Join(" | ", analyzer.Errors.Select(e => e.Message)));

        var cs = new CSharpCodeGenerator().Generate(new IRBuilder(analyzer).Build(ast, "T"));

        Assert.That(cs, Does.Match(expected));
    }

    /// <summary>
    /// A property with an explicit `End Property` but no Get or Set is still an ERROR. The
    /// auto-property exemption keys on the parser's IsAuto marker, not on "both accessors
    /// are null" — those two states are otherwise indistinguishable in the AST.
    /// </summary>
    [Test]
    public void EmptyExplicitProperty_IsStillRejected()
    {
        var src =
            "Class C\nPublic Property V As Integer\nEnd Property\nEnd Class\nSub Main()\nEnd Sub";

        var ast = Parse(src, out _);
        var analyzer = new SemanticAnalyzer();

        Assert.That(analyzer.Analyze(ast), Is.False,
            "an explicitly-opened property body with no accessors must remain an error");
        Assert.That(string.Join(" | ", analyzer.Errors.Select(e => e.Message)),
            Does.Contain("at least one accessor"));
    }

    /// <summary>An explicit Get/Set property must keep working exactly as before.</summary>
    [Test]
    public void ExplicitProperty_StillParsesAndKeepsItsBody()
    {
        var src =
            "Class C\n" +
            "Private _v As Integer\n" +
            "Public Property V As Integer\n" +
            "Get\n" +
            "Return _v\n" +
            "End Get\n" +
            "End Property\n" +
            "End Class\n" +
            "Sub Main()\nEnd Sub";

        var ast = Parse(src, out var parser);

        Assert.That(parser.Errors, Is.Empty,
            string.Join(" | ", parser.Errors.Select(e => e.ToString())));
        Assert.That(ast.Declarations, Has.Count.EqualTo(2));
    }
}
