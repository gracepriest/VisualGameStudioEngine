using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan 2, Task 7 — <c>::</c> is COMPLETE: a foreign member can be assigned, and a foreign
/// value can be stored in a local, inferred or typed.
///
/// <para><b>Before this, <c>::</c> was call-only</b>, pinned by three <c>_KNOWN</c> tests in
/// <see cref="JavaScriptInteropTests"/> (now inverted here): <c>::document.title = "hi"</c> was
/// a SemanticAnalyzer error ("Cannot assign value of type 'String' to '::document::title'"),
/// <c>Dim el = ::document.getElementById("x")</c> was refused by ForeignFeatureChecker's
/// declared-type walk (the local's INFERRED type is Foreign), and
/// <c>Dim v As Integer = ::getValue()</c> died in the analyzer's initializer check. Every
/// stateful DOM idiom therefore needed <c>javascript{ }</c> — the first wall a user hit.</para>
///
/// <para><b>The principle.</b> A foreign member has no knowable type, so an assignment to it has
/// nothing to check; a foreign VALUE likewise converts to whatever it is stored in. That is a
/// statement about <c>::</c> the language feature, so it lives in the analyzer. What stays
/// refused: an ANNOTATED foreign type (<c>Dim m As std::mutex</c>) — a C++ type genuinely does
/// not lower — and every non-JavaScript backend, which never asked for the relaxation.</para>
///
/// <para>Executed under Node with a <c>javascript{ }</c> prelude standing in for the DOM: a
/// <c>globalThis.box</c> object with a property, a nested object and a method.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class JavaScriptForeignStateTests
{
    private const string Prelude =
        "javascript{ globalThis.box = { title: \"a\", count: 41, inner: { value: 1 }, bump() { this.count = this.count + 1; return this.count; } }; }\n";

    private static string Run(string body) =>
        JavaScriptExecutionTests.RunJs($"Sub Main()\n{Prelude}{body}\nEnd Sub");

    // ---------------------------------------------------------------- member assignment

    [Test]
    public void ForeignMemberAssignment_Writes()
        => Assert.That(Run("::box.title = \"hi\"\nConsole.WriteLine(::box.title)"), Is.EqualTo("hi"));

    [Test]
    public void NestedForeignMemberAssignment_Writes()
        => Assert.That(Run("::box.inner.value = 5\nConsole.WriteLine(::box.inner.value)"), Is.EqualTo("5"));

    /// <summary>The value side can be any BasicLang expression, not just a literal.</summary>
    [Test]
    public void ForeignMemberAssignment_FromAnExpression()
        => Assert.That(Run("Dim n As Integer = 20\n::box.count = n * 2\nConsole.WriteLine(::box.count)"), Is.EqualTo("40"));

    // ---------------------------------------------------------------- storing a foreign value

    [Test]
    public void InferredLocal_HoldsAForeignValue_AndItsMembersAreReachable()
        => Assert.That(Run("Dim b = ::box\nConsole.WriteLine(b.title)\nb.title = \"x\"\nConsole.WriteLine(::box.title)"),
            Is.EqualTo("a\nx"));

    [Test]
    public void InferredLocal_FromAForeignMemberChain()
        => Assert.That(Run("Dim i = ::box.inner\ni.value = 9\nConsole.WriteLine(::box.inner.value)"), Is.EqualTo("9"));

    [Test]
    public void InferredLocal_FromAForeignCall_CanBeCalledAgain()
        => Assert.That(Run("Dim b = ::box\nb.bump()\nConsole.WriteLine(b.bump())"), Is.EqualTo("43"));

    /// <summary>A TYPED local: the foreign value converts to the declared type, and the local is ordinary from then on.</summary>
    [Test]
    public void TypedLocal_HoldsAForeignValue()
        => Assert.That(Run("Dim n As Integer = ::box.count\nConsole.WriteLine(n + 1)"), Is.EqualTo("42"));

    [Test]
    public void TypedLocal_FromAForeignCall()
        => Assert.That(Run("Dim n As Integer = ::box.bump()\nConsole.WriteLine(n + 1)"), Is.EqualTo("43"));

    [Test]
    public void ForeignValue_AssignedToAnExistingTypedLocal()
        => Assert.That(Run("Dim s As String\ns = ::box.title\nConsole.WriteLine(s & \"!\")"), Is.EqualTo("a!"));

    // ---------------------------------------------------------------- the SHIPPING IR

    [TestCase("::box.title = \"hi\"\nConsole.WriteLine(::box.title)", "hi")]
    [TestCase("Dim b = ::box\nb.title = \"x\"\nConsole.WriteLine(::box.title)", "x")]
    [TestCase("Dim n As Integer = ::box.count\nConsole.WriteLine(n + 1)", "42")]
    public void Optimized_ForeignState(string body, string expected)
        => Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized($"Sub Main()\n{Prelude}{body}\nEnd Sub"),
            Is.EqualTo(expected));
}

/// <summary>Codegen-side: the member is emitted VERBATIM, and the type-position refusal survives.</summary>
[TestFixture]
public class JavaScriptForeignStateCodeGenTests
{
    [Test]
    public void ForeignMemberAssignment_EmitsTheMemberVerbatim()
        => Assert.That(JsTestSupport.Compile("Sub Main()\n::document.title = \"hi\"\nEnd Sub"),
            Does.Contain("document.title = \"hi\";"));

    /// <summary>`Me` is the member's NAME here, not `this` — the same rule FieldAccess already applies on reads.</summary>
    [Test]
    public void ForeignMemberAssignment_DoesNotSanitizeTheMemberName()
        => Assert.That(JsTestSupport.Compile("Sub Main()\n::obj.Me = 1\nEnd Sub"),
            Does.Contain("obj.Me = 1;"));

    [Test]
    public void InferredForeignLocal_Compiles()
        => Assert.That(JsTestSupport.Compile("Sub Main()\nDim el = ::document.getElementById(\"out\")\nel.textContent = \"hi\"\nEnd Sub"),
            Does.Contain("el.textContent = \"hi\";"));

    /// <summary>An ANNOTATED foreign type is a C++ type and still does not lower — the relaxation is for inferred locals only.</summary>
    [Test]
    public void AnnotatedForeignType_IsStillRejected()
        => Assert.That(() => JsTestSupport.Compile("Sub Main()\nDim m As std::mutex\nEnd Sub"),
            Throws.TypeOf<BasicLang.Compiler.CodeGen.ForeignFeatureException>());
}
