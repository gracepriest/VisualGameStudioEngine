using System;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.JavaScript;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 5: the JavaScript backend joins the honesty matrix.
///
/// <para>JavaScript's row: <c>#CppInclude</c> ❌ error, <c>::</c> foreign <b>TYPES</b> ❌ error,
/// <c>::</c> foreign <b>EXPRESSIONS</b> ✅ emitted verbatim (plan 2 task 3),
/// collections ✅ native (List/Dictionary lower to Array/Map, spec).</para>
///
/// <para>⛔ <b>The type/expression split is deliberate, not an oversight.</b> <c>::</c> means two
/// different things by POSITION. In EXPRESSION position it is a raw JavaScript identifier
/// (<c>::console.log(x)</c>) and lowers verbatim — that is the JS interop escape hatch, covered in
/// <see cref="JavaScriptInteropTests"/>. In TYPE position it is an opaque C++ passthrough type
/// (<c>Dim m As std::mutex</c>) with no JavaScript meaning at all; emitting it would mangle to
/// <c>stdmutex</c>, an undefined identifier. So the two sit side by side below, one rejected and
/// one not, and neither is a bug in the other.</para>
///
/// <para>The rejection has to happen at BUILD time. Every open C++ backend bug is a
/// feature that LOOKED supported and produced silently wrong output at runtime; the whole
/// premise of this backend is that a refusal beats a half implementation.</para>
/// </summary>
[TestFixture]
public class JsCapabilityCheckerTests
{
    [Test]
    public void Js_ForeignType_ThrowsCleanError()
    {
        var module = JsTestSupport.BuildModule("Sub Main()\nDim m As std::mutex\nEnd Sub");

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new JavaScriptCodeGenerator().Generate(module));

        Assert.That(ex!.Message, Does.Contain("JavaScript"));
        Assert.That(ex.Message, Does.Contain("std::mutex"));
    }

    /// <summary>
    /// The sibling of <see cref="Js_ForeignType_ThrowsCleanError"/>, kept adjacent so the pair
    /// reads as one deliberate distinction rather than two unrelated tests: the SAME <c>::</c>
    /// syntax in EXPRESSION position is accepted and emitted verbatim.
    /// </summary>
    [Test]
    public void Js_ForeignExpression_IsAccepted()
    {
        var module = JsTestSupport.BuildModule("Sub Main()\n::console.log(\"hi\")\nEnd Sub");

        Assert.That(new JavaScriptCodeGenerator().Generate(module),
            Does.Contain("console.log(\"hi\")"));
    }

    /// <summary>
    /// ⛔ REGRESSION GUARD for the five-backend seam. <c>ForeignFeatureChecker</c> is shared, and
    /// the relaxation that lets <c>::</c> through is opt-in per backend
    /// (<c>allowForeignIdentifiers</c>). C#, LLVM and MSIL have no way to lower a raw JavaScript
    /// identifier, so they must keep refusing it outright — if the flag ever became the default,
    /// or a caller were updated by copy-paste, they would silently strip the <c>::</c> and emit an
    /// undefined identifier from a green build. That is the exact bug the guard was written for.
    ///
    /// <para>Driven through <c>Program.GenerateCode</c> rather than by constructing the three
    /// generators directly: that is the CLI's real dispatch seam, so this also proves the wiring
    /// each backend actually ships with.</para>
    /// </summary>
    [TestCase("csharp")]
    [TestCase("llvm")]
    [TestCase("msil")]
    public void ForeignIdentifier_IsStillRejectedOnOtherBackends(string backend)
    {
        var module = JsTestSupport.BuildModule("Sub Main()\n::console.log(\"hi\")\nEnd Sub");

        var ex = Assert.Throws<ForeignFeatureException>(
            () => BasicLang.Compiler.Driver.Program.GenerateCode(module, backend));
        Assert.That(ex!.Message, Does.Contain("::"));
    }

    [Test]
    public void Js_CppInclude_ThrowsCleanError()
    {
        var module = JsTestSupport.BuildModule("#CppInclude <mutex>\nSub Main()\nEnd Sub",
            runPreprocessor: true);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new JavaScriptCodeGenerator().Generate(module));

        Assert.That(ex!.Message, Does.Contain("JavaScript"));
    }

    /// <summary>
    /// Plan task 6, reframed: BL7001 was to reject method overloading, but BasicLang has
    /// none — the SemanticAnalyzer already refuses a second same-named subroutine, for free
    /// subs and class methods alike. A BL7001 arm in JsCapabilityChecker could never fire,
    /// so the diagnostic was dropped and this guard pins the assumption instead.
    ///
    /// <para>Passes the moment it is written, deliberately. If BasicLang ever gains
    /// overloading this goes red, which is the signal that the JavaScript backend needs a
    /// real BL7001 after all — JS has no overload resolution and the last definition would
    /// silently win. Without this test that reasoning survives only in a commit message.</para>
    /// </summary>
    [TestCase("Sub F(a As Integer)\nEnd Sub\nSub F(a As String)\nEnd Sub\nSub Main()\nEnd Sub",
        TestName = "Overloading_FreeSubs_RejectedByFrontEnd")]
    [TestCase("Class C\nPublic Sub F(a As Integer)\nEnd Sub\nPublic Sub F(a As String)\nEnd Sub\nEnd Class\nSub Main()\nEnd Sub",
        TestName = "Overloading_ClassMethods_RejectedByFrontEnd")]
    public void Overloading_IsRejectedByTheFrontEnd_SoJsNeedsNoDiagnostic(string source)
    {
        var analyzer = new BasicLang.Compiler.SemanticAnalysis.SemanticAnalyzer();
        var ast = new BasicLang.Compiler.Parser(new BasicLang.Compiler.Lexer(source).Tokenize()).Parse();

        Assert.That(analyzer.Analyze(ast), Is.False,
            "BasicLang gained method overloading — the JavaScript backend now needs BL7001, " +
            "because JS has no overload resolution and the last definition silently wins.");
        Assert.That(string.Join(" | ", analyzer.Errors.ConvertAll(e => e.Message)),
            Does.Contain("already defined"));
    }

    /// <summary>
    /// Plan task 7 — BL7002. JavaScript has no reference parameters: a primitive is copied
    /// on call, so a ByRef write is invisible to the caller.
    ///
    /// <para>This is the highest-value rejection in Phase 1 because it is a bug the C++
    /// backend actually shipped: `Sub Bump(ByRef x)` lowers there to `void Bump(int32_t x)`
    /// and prints 1 instead of 11 — a build that succeeds and silently computes the wrong
    /// answer. Rejecting it is what stops the JavaScript backend inheriting that bug class.</para>
    /// </summary>
    [Test]
    public void Js_ByRefParameter_IsRejected()
    {
        var module = JsTestSupport.BuildModule(
            "Sub Bump(ByRef x As Integer)\nx = x + 1\nEnd Sub\nSub Main()\nEnd Sub");

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new JavaScriptCodeGenerator().Generate(module));

        Assert.That(ex!.Message, Does.Contain("BL7002"));
        Assert.That(ex.Message, Does.Contain("ByRef"));
        Assert.That(ex.Message, Does.Contain("x"), "must name the offending parameter");
        Assert.That(ex.Message, Does.Contain("Bump"), "must name the function it is in");
    }

    /// <summary>
    /// Class methods flatten into module.Functions carrying IsByRef, so one walk covers
    /// them — but only if the walk really is over the flattened functions. A checker that
    /// looked at IRClass.Methods instead would find nothing: those parameter lists are
    /// empty, the parameters live on IRMethod.Implementation.
    /// </summary>
    [Test]
    public void Js_ByRefParameter_OnAClassMethod_IsAlsoRejected()
    {
        var module = JsTestSupport.BuildModule(
            "Class C\nPublic Sub Bump(ByRef x As Integer)\nEnd Sub\nEnd Class\nSub Main()\nEnd Sub");

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new JavaScriptCodeGenerator().Generate(module));

        Assert.That(ex!.Message, Does.Contain("BL7002"));
    }

    /// <summary>
    /// ByRef reaches the IR through TWO unrelated types: IRVariable (functions, class
    /// methods) and IRParameter (interface methods, delegates, extern declarations). A
    /// checker that walks only one of them leaves the other silently accepted, so these
    /// pin the IRParameter side.
    /// </summary>
    [TestCase("Interface I\nSub F(ByRef x As Integer)\nEnd Interface\nSub Main()\nEnd Sub",
        TestName = "ByRef_OnAnInterfaceMethod_IsRejected")]
    [TestCase("Delegate Sub D(ByRef x As Integer)\nSub Main()\nEnd Sub",
        TestName = "ByRef_OnADelegate_IsRejected")]
    public void Js_ByRef_ThroughTheIRParameterChannel_IsRejected(string source)
    {
        var module = JsTestSupport.BuildModule(source);

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new JavaScriptCodeGenerator().Generate(module));

        Assert.That(ex!.Message, Does.Contain("BL7002"));
    }

    /// <summary>
    /// The call-site ByRef guard in Visit(IRCall) is defence in depth, NOT dead code, and
    /// must not be deleted now that BL7002 rejects ByRef declarations.
    ///
    /// <para>IRCall.ByRefArguments is populated from TWO sources (IRBuilder.cs): a resolved
    /// USER function's IsByRef (line 3721), which BL7002 catches at the declaration; and a
    /// resolved .NET target's RefKind (line 3585), where the by-ref fact lives in the .NET
    /// descriptor and NO BasicLang declaration carries IsByRef at all. The declaration walk
    /// cannot see that second channel.</para>
    ///
    /// <para>Simulated here by clearing IsByRef on the declaration while leaving the call's
    /// ByRefArguments set — exactly the shape a .NET ref/out argument produces. Until
    /// BL7007 rejects .NET types wholesale, this guard is the only thing standing between
    /// that call and a by-value emit that silently discards the write-back.</para>
    /// </summary>
    [Test]
    public void Js_ByRefAtTheCallSite_IsRejected_EvenWhenTheDeclarationDoesNotSayByRef()
    {
        var module = JsTestSupport.BuildModule(
            "Sub Bump(ByRef x As Integer)\nEnd Sub\n" +
            "Sub Main()\nDim v As Integer\nBump(v)\nEnd Sub");

        // Strip the declaration-level flag, leaving only the call-site fact.
        foreach (var f in module.Functions)
            foreach (var p in f.Parameters)
                p.IsByRef = false;

        var ex = Assert.Throws<ForeignFeatureException>(
            () => new JavaScriptCodeGenerator().Generate(module));

        Assert.That(ex!.Message, Does.Contain("BL7002"),
            "a by-ref argument must be refused as BL7002, not reported as an unimplemented " +
            "feature — it is rejected by design and will never be implemented.");
    }

    /// <summary>A ByVal parameter is ordinary and must not be caught by the ByRef arm.</summary>
    [Test]
    public void Js_ByValParameter_IsNotRejected()
    {
        var module = JsTestSupport.BuildModule(
            "Sub Ok(a As Integer)\nEnd Sub\nSub Main()\nEnd Sub");

        Assert.DoesNotThrow(() => new JavaScriptCodeGenerator().Generate(module));
    }

    /// <summary>
    /// Collections are IN for JavaScript, so the checker must not reject them.
    ///
    /// <para>Asserts on the exception TYPE rather than using DoesNotThrow: collection
    /// codegen does not land until task 18, so a NotSupportedException from an
    /// unimplemented visitor is the expected state right now. Only a
    /// ForeignFeatureException would be wrong — that would mean the backend had been
    /// wired like LLVM/MSIL, which reject collections outright.</para>
    /// </summary>
    [Test]
    public void Js_Collections_AreNotRejectedByTheCapabilityChecker()
    {
        var module = JsTestSupport.BuildModule("Sub Main()\nDim l As New List(Of Integer)()\nEnd Sub");

        Exception caught = null;
        try { new JavaScriptCodeGenerator().Generate(module); }
        catch (Exception e) { caught = e; }

        Assert.That(caught, Is.Not.InstanceOf<ForeignFeatureException>(),
            "List/Dictionary are supported on JavaScript (Array/Map). The backend must pass " +
            "rejectCollections: false, like C# does — not true, like LLVM/MSIL.");
    }
}
