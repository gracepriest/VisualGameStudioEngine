using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>Extern Class</c> — a declaration-only type: it already exists in the target runtime, so
/// nothing is emitted for it and its members are signatures rather than bodies.
///
/// <para><b>Why this exists.</b> Before it, every DOM idiom was either a <c>::</c> passthrough
/// (call-only — a member assignment dies in the SemanticAnalyzer and a <c>::</c> value cannot be
/// stored) or an unchecked <c>javascript{ }</c> block. Neither is type-checked. This is the first
/// way to reach a foreign object WITH checking.</para>
///
/// <para><b><c>Extern</c> is deliberately overloaded.</b> <c>Extern Function</c> with a body says
/// HOW to define something per backend; <c>Extern Class</c> with no body says it is ALREADY
/// defined. Both read as "defined outside BasicLang" — no new keyword, no new concept.</para>
///
/// <para>⛔ Member names keep their EXACT JavaScript casing (<c>textContent</c>, not
/// <c>TextContent</c>), and codegen emits the declared name verbatim. A "corrected" PascalCase
/// name would call a member the runtime does not have and silently yield <c>undefined</c>.</para>
/// </summary>
[TestFixture]
public class ExternClassTests
{
    /// <summary>
    /// Parses only — no semantic analysis, so a parse failure stays distinguishable from a
    /// semantic one.
    ///
    /// <para>⛔ Asserting on <c>parser.Errors</c> is not optional. <c>Parser.Parse()</c> CATCHES
    /// ParseException internally, records it, and Synchronize()s forward — so a file with a
    /// syntax error still returns normally, usually with ZERO declarations. A test that only
    /// looked at the returned tree would find nothing and pass vacuously.</para>
    /// </summary>
    private static ProgramNode Parse(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors.Select(e => e.ToString()), Is.Empty, "parse errors");
        return ast;
    }

    internal const string ElementDecl =
        "Extern Class Element\n" +
        "Public Property textContent As String\n" +
        "Public Function querySelector(sel As String) As Element\n" +
        "Public Sub addEventListener(evt As String, handler As Action)\n" +
        "End Class\n";

    [Test]
    public void ExternClass_Parses()
    {
        var cls = Parse(ElementDecl + "Sub Main()\nEnd Sub").Declarations
            .OfType<ClassNode>().Single();

        Assert.That(cls.Name, Is.EqualTo("Element"));
        Assert.That(cls.IsExtern, Is.True);
    }

    /// <summary>
    /// ⛔ THE SECOND PARSER EDIT. <c>Public Extern Class</c> goes through a DIFFERENT block
    /// (the modifier path), which has no <c>Extern</c> in its Check(...) set and no Extern arm
    /// after the modifier loop. Bare <c>Extern Class</c> passing proves nothing about this one.
    /// </summary>
    [Test]
    public void PublicExternClass_Parses()
    {
        var cls = Parse("Public " + ElementDecl + "Sub Main()\nEnd Sub").Declarations
            .OfType<ClassNode>().Single();

        Assert.That(cls.IsExtern, Is.True);
        Assert.That(cls.Access, Is.EqualTo(AccessModifier.Public));
    }

    /// <summary>
    /// Members are SIGNATURES: no bodies, no <c>End Function</c>, no <c>End Property</c>.
    /// This is the shape an Interface already has, which is why the interface member parsers
    /// are reused rather than duplicated.
    /// </summary>
    [Test]
    public void ExternClass_MembersAreSignaturesOnly()
    {
        var cls = Parse(ElementDecl + "Sub Main()\nEnd Sub").Declarations
            .OfType<ClassNode>().Single();

        Assert.That(cls.Members.OfType<PropertyNode>().Select(p => p.Name),
            Is.EquivalentTo(new[] { "textContent" }));
        Assert.That(cls.Members.OfType<FunctionNode>().Select(m => m.Name),
            Is.EquivalentTo(new[] { "querySelector", "addEventListener" }));
    }

    /// <summary>
    /// ⛔ Member names keep their EXACT JavaScript casing — camelCase AND an all-caps acronym,
    /// because a naive "lowercase the first letter" rule handles the first and mangles the
    /// second (<c>URL</c> would become <c>uRL</c>). Exactness by construction is why there is no
    /// conversion rule and no Alias syntax.
    /// </summary>
    [Test]
    public void ExternClass_MemberCasingIsPreservedExactly()
    {
        var cls = Parse("Extern Class Doc\nPublic Property nodeValue As String\n" +
                        "Public Property URL As String\nEnd Class\nSub Main()\nEnd Sub")
            .Declarations.OfType<ClassNode>().Single();

        Assert.That(cls.Members.OfType<PropertyNode>().Select(p => p.Name),
            Is.EquivalentTo(new[] { "nodeValue", "URL" }));
    }

    /// <summary>An ordinary class is unaffected — IsExtern is opt-in.</summary>
    [Test]
    public void OrdinaryClass_IsNotExtern()
        => Assert.That(Parse("Class Box\nPublic X As Integer\nEnd Class\nSub Main()\nEnd Sub")
            .Declarations.OfType<ClassNode>().Single().IsExtern, Is.False);

    // ------------------------------------------------------------------
    // A member WITH a body.
    //
    // ⛔ It was ALREADY refused before this was written — but by the generic
    // "Unexpected token in class: 'Console'" arm, whose suggestion lists the valid member
    // kinds. The user wrote a perfectly ordinary Sub body; nothing told them an extern member
    // cannot have one, so the message taught the wrong model. A refusal that misdirects is
    // barely better than no refusal.
    // ------------------------------------------------------------------

    private static string[] ParseErrors(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        parser.Parse();
        return parser.Errors.Select(e => e.ToString()).ToArray();
    }

    [Test]
    public void ExternMemberWithABody_IsRefusedAndSaysWhy()
    {
        var errors = string.Join(" | ", ParseErrors(
            "Extern Class Element\nPublic Sub click()\nConsole.WriteLine(1)\nEnd Sub\n" +
            "End Class\nSub Main()\nEnd Sub"));

        Assert.That(errors, Is.Not.Empty, "a body that can never run must not be accepted");
        Assert.That(errors, Does.Contain("Extern"),
            "the diagnostic must name the thing that makes a body invalid here");
    }

    /// <summary>The same body in an ORDINARY class is still perfectly legal.</summary>
    [Test]
    public void OrdinaryMemberWithABody_IsStillAccepted()
        => Assert.That(ParseErrors(
            "Class Box\nPublic Sub click()\nConsole.WriteLine(1)\nEnd Sub\nEnd Class\n" +
            "Sub Main()\nEnd Sub"), Is.Empty);

    // ------------------------------------------------------------------
    // The flag has to REACH the backends, which read IR and never see the AST.
    // ------------------------------------------------------------------

    [Test]
    public void ExternClass_IsExternSurvivesToTheIr()
        => Assert.That(JsTestSupport.BuildModule(ElementDecl + "Sub Main()\nEnd Sub")
            .Classes["Element"].IsExtern, Is.True);

    [Test]
    public void OrdinaryClass_IsNotExternInTheIr()
        => Assert.That(JsTestSupport.BuildModule(
            "Class Box\nPublic X As Integer\nEnd Class\nSub Main()\nEnd Sub")
            .Classes["Box"].IsExtern, Is.False);
}
