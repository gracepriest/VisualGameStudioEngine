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

    // ==================================================================
    // EMISSION — the highest-value part of the feature.
    //
    // ⛔ MEASURED before the fix, and it was worse than "emits a redundant class":
    //
    //     class Element {
    //         textContent = "";
    //         querySelector(sel) { return null; }   // <-- a SYNTHESIZED body
    //     }
    //
    // Two independent failures from one green build. The declaration SHADOWS the real
    // runtime type, and every extern method gets a stub that RETURNS NULL — so
    // `el.querySelector("p")` would quietly yield nothing instead of reaching the DOM.
    // ==================================================================

    private const string UseElement =
        ElementDecl + "Sub Main()\nDim e As Element\nConsole.WriteLine(e.textContent)\nEnd Sub";

    [Test]
    public void ExternClass_EmitsNoClassDeclaration()
        => Assert.That(JsTestSupport.Compile(UseElement), Does.Not.Contain("class Element"),
            "emitting the declaration SHADOWS the real runtime type — a runtime failure with " +
            "no compile error");

    /// <summary>
    /// ⛔ The stub bodies are the sharper half of the bug. A shadowing class might still
    /// happen to work if it were empty; `querySelector(sel) { return null; }` cannot — it
    /// silently answers null for a call that was supposed to reach the runtime.
    /// </summary>
    [Test]
    public void ExternClass_EmitsNoSynthesizedMemberBodies()
    {
        var js = JsTestSupport.Compile(UseElement);

        Assert.That(js, Does.Not.Contain("querySelector(sel)"),
            "a synthesized stub would answer null instead of calling the real member");
        Assert.That(js, Does.Not.Contain("textContent = \"\""),
            "a field initialiser would overwrite the runtime object's own property");
    }

    /// <summary>A member ACCESS still emits, by the declared name, verbatim.</summary>
    [Test]
    public void ExternClass_MemberAccessEmitsTheDeclaredName()
        => Assert.That(JsTestSupport.Compile(UseElement), Does.Contain(".textContent"));

    /// <summary>
    /// ⭐ THE PAYOFF. A BasicLang user typing <c>.TextContent</c> out of PascalCase habit must
    /// still emit <c>.textContent</c> — BasicLang is case-insensitive, JavaScript is not, and
    /// the declared spelling is the only correct one. This works because member names are
    /// canonicalised to the declaration at IR-build time; without that, this reads a property
    /// the runtime object does not have and yields undefined.
    /// </summary>
    [Test]
    public void ExternClass_UseSiteCasingIsCanonicalisedToTheDeclaredName()
    {
        var js = JsTestSupport.Compile(
            ElementDecl + "Sub Main()\nDim e As Element\nConsole.WriteLine(e.TextContent)\nEnd Sub");

        Assert.That(js, Does.Contain(".textContent"));
        Assert.That(js, Does.Not.Contain(".TextContent"));
    }

    /// <summary>An ORDINARY class must still be emitted — the skip is opt-in, not a blanket.</summary>
    [Test]
    public void OrdinaryClass_IsStillEmitted()
        => Assert.That(JsTestSupport.Compile(
            "Class Box\nPublic X As Integer\nEnd Class\nSub Main()\nDim b As New Box()\nEnd Sub"),
            Does.Contain("class Box"));

    // ---- the SHIPPING IR. Every route optimizes unconditionally; JsTestSupport.Compile does not.

    [Test]
    public void Optimized_ExternClass_StillEmitsNoDeclaration()
        => Assert.That(JsTestSupport.CompileOptimized(UseElement),
            Does.Not.Contain("class Element"));

    [Test]
    public void Optimized_OrdinaryClass_IsStillEmitted()
        => Assert.That(JsTestSupport.CompileOptimized(
            "Class Box\nPublic X As Integer\nEnd Class\nSub Main()\nDim b As New Box()\nEnd Sub"),
            Does.Contain("class Box"));

    // ------------------------------------------------------------------
    // `New` on an extern class.
    //
    // Nothing is emitted for the type, so there is no constructor to call. Left alone this
    // reaches the browser as `Element is not a constructor` — a runtime error from a green
    // build, which is the shape this backend refuses everywhere else.
    // ------------------------------------------------------------------

    [Test]
    public void ExternClass_New_IsRefused()
        => Assert.That(() => JsTestSupport.Compile(
                ElementDecl + "Sub Main()\nDim e As New Element()\nEnd Sub"),
            Throws.Exception,
            "nothing is emitted for an extern type, so there is no constructor to call");

    /// <summary>
    /// ⛔ The EXPRESSION-TEMPORARY shape, which binds to no declared local. That split — a
    /// declared position versus a transient — is exactly where ForeignFeatureChecker documents
    /// its own guards having had a blind spot, so it is checked separately rather than assumed
    /// to be covered by the Dim case.
    /// </summary>
    [Test]
    public void ExternClass_NewAsAnExpressionTemporary_IsRefused()
        => Assert.That(() => JsTestSupport.Compile(
                ElementDecl + "Sub Main()\nConsole.WriteLine(New Element())\nEnd Sub"),
            Throws.Exception);

    /// <summary>`New` on an ORDINARY class is of course untouched.</summary>
    [Test]
    public void OrdinaryClass_New_IsStillAllowed()
        => Assert.That(JsTestSupport.Compile(
            "Class Box\nPublic X As Integer\nEnd Class\nSub Main()\nDim b As New Box()\nEnd Sub"),
            Does.Contain("new Box()"));

    // ------------------------------------------------------------------
    // ⛔ THE MIRROR TEST: does the OTHER backend refuse it?
    //
    // On this backend that question has found a silently-dropped feature EVERY time it has
    // been asked — #JsImport on C#/C++, and foreign inline blocks on C++ (broken since they
    // existed). An Extern Class names a type in a JavaScript runtime; C# and C++ have no such
    // type, so emitting or ignoring the declaration are both wrong.
    // ------------------------------------------------------------------

    [TestCase("csharp")]
    [TestCase("cpp")]
    public void ExternClass_IsRejectedOnOtherBackends(string backend)
    {
        var module = JsTestSupport.BuildModule(ElementDecl + "Sub Main()\nEnd Sub");

        Assert.That(() => BasicLang.Compiler.Driver.Program.GenerateCode(module, backend),
            Throws.Exception,
            $"the {backend} backend silently accepted a type that only exists in a JS runtime");
    }

    [Test]
    public void ExternClass_IsAcceptedOnJavaScript()
        => Assert.DoesNotThrow(() => JsTestSupport.Compile(ElementDecl + "Sub Main()\nEnd Sub"));

    /// <summary>An ORDINARY class must still compile on those backends.</summary>
    [TestCase("csharp")]
    [TestCase("cpp")]
    public void OrdinaryClass_IsStillAcceptedOnOtherBackends(string backend)
    {
        var module = JsTestSupport.BuildModule(
            "Class Box\nPublic X As Integer\nEnd Class\nSub Main()\nEnd Sub");

        Assert.DoesNotThrow(() => BasicLang.Compiler.Driver.Program.GenerateCode(module, backend));
    }

    // ------------------------------------------------------------------
    // BL7011 — an extern name that collides with the stdlib surface.
    //
    // ⛔ `console` and `Console` are THE SAME KEY: IRModule.Classes is OrdinalIgnoreCase, and
    // Console is already the stdlib surface (Console.WriteLine lowers to console.log). The
    // exact-JavaScript-name rule that makes every other extern declaration correct does NOT
    // save you here — declaring `Extern Class console` looks distinct and is not.
    //
    // The right answer is to leave `console` out of any DOM declarations entirely; this
    // diagnostic is what says so instead of letting the collision act at a distance.
    // ------------------------------------------------------------------

    [Test]
    public void ExternClass_CollidingWithTheStdLibSurface_IsRejected()
        => Assert.That(() => JsTestSupport.Compile(
                "Extern Class console\nPublic Sub log(msg As String)\nEnd Class\n" +
                "Sub Main()\nEnd Sub"),
            Throws.Exception.With.Message.Contains("BL7011"));

    /// <summary>The message must say what to do instead, not merely refuse.</summary>
    [Test]
    public void ExternClass_CollisionMessage_SaysTheStdLibAlreadyCoversIt()
        => Assert.That(() => JsTestSupport.Compile(
                "Extern Class console\nPublic Sub log(msg As String)\nEnd Class\n" +
                "Sub Main()\nEnd Sub"),
            Throws.Exception.With.Message.Contains("Console"));

    /// <summary>An unrelated extern name is of course fine.</summary>
    [Test]
    public void ExternClass_WithAnUnrelatedName_IsNotACollision()
        => Assert.DoesNotThrow(() => JsTestSupport.Compile(ElementDecl + "Sub Main()\nEnd Sub"));

    /// <summary>
    /// ⚠ An ORDINARY class named `console` collides just as hard — the case-insensitive key is
    /// a property of the IR, not of Extern. Pinned so the diagnostic is not accidentally
    /// narrowed to extern declarations later.
    /// </summary>
    [Test]
    public void OrdinaryClass_CollidingWithTheStdLibSurface_IsAlsoRejected()
        => Assert.That(() => JsTestSupport.Compile(
                "Class console\nPublic X As Integer\nEnd Class\nSub Main()\nEnd Sub"),
            Throws.Exception.With.Message.Contains("BL7011"));

    /// <summary>
    /// Every name on the surface, not just the one that motivated the rule. `Math` is the one
    /// a user is most likely to reach for innocently.
    /// </summary>
    [TestCase("Math")]
    [TestCase("Random")]
    [TestCase("Regex")]
    [TestCase("DateTime")]
    public void ExternClass_AnyStdLibSurfaceName_IsRejected(string name)
        => Assert.That(() => JsTestSupport.Compile(
                $"Extern Class {name}\nPublic Sub f()\nEnd Class\nSub Main()\nEnd Sub"),
            Throws.Exception.With.Message.Contains("BL7011"));

    /// <summary>
    /// The exception-by-suffix rule is GONE. BL7007's allow-list used to admit ANY name ending
    /// in "Exception", on the promise that the generator erased it to <c>Error</c> — which it
    /// never did, so <c>Throw New SomeUndeclaredException()</c> was a ReferenceError from a green
    /// build. An undeclared exception name is now BL7012, with the provided hierarchy on the
    /// allow-list instead (JsExceptionTypes). For plan 2c this is the better world: a GENERATED
    /// declaration file that omits e.g. DOMException is caught at build time, not in a browser.
    /// </summary>
    [Test]
    public void UndeclaredExceptionSuffixedName_IsRejected_BL7012()
        => Assert.That(() => JsTestSupport.Compile(
                "Sub F(e As SomeUndeclaredException)\nEnd Sub\nSub Main()\nEnd Sub"),
            Throws.Exception.With.Message.Contains("BL7012"));

    /// <summary>…and an Extern Class IS a declaration, so a runtime-provided exception type is admitted through it.</summary>
    [Test]
    public void ExternDeclaredExceptionType_IsAdmitted()
        => Assert.DoesNotThrow(() => JsTestSupport.Compile(
            "Extern Class DOMException\nPublic Function ToString() As String\nEnd Class\n" +
            "Sub F(e As DOMException)\nEnd Sub\nSub Main()\nEnd Sub"));
}
