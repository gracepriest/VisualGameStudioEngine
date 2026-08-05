using System;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.JavaScript;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 5: the JavaScript backend joins the honesty matrix.
///
/// <para>JavaScript's row: <c>#CppInclude</c> ❌ error, <c>::</c> foreign types ❌ error,
/// collections ✅ native (List/Dictionary lower to Array/Map, spec).</para>
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
