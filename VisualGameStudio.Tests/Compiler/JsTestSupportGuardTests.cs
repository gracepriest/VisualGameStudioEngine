using System;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The JavaScript fixtures' front-end driver must REFUSE source that does not compile,
/// rather than quietly handing back whatever partial IR it managed to build.
///
/// <para><b>Why this guard exists.</b> <c>Parser.Parse()</c> CATCHES ParseException
/// internally: it records the error and <c>Synchronize()</c>s forward, so a source file with
/// a syntax error still returns normally — often with ZERO declarations. The
/// SemanticAnalyzer then reports success (an empty program is valid) and IRBuilder produces
/// an empty module. A fixture whose source has a typo would therefore assert happily against
/// a module containing nothing, and PASS.</para>
///
/// <para>That is a false-green generator, and it is the same failure shape as the raylib DLL
/// staging trap: a tier that proves nothing while reading as green. Every capability
/// rejection in this backend is asserted through this helper, so a silent parse failure
/// would make those rejections untestable without anyone noticing.</para>
/// </summary>
[TestFixture]
public class JsTestSupportGuardTests
{
    /// <summary>An unterminated class: a parse error that will never become valid syntax.</summary>
    private const string Unparseable = "Class C\nSub Main()\nEnd Sub";

    [Test]
    public void BuildModule_RefusesSourceWithParseErrors()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => JsTestSupport.BuildModule(Unparseable));

        Assert.That(ex!.Message, Does.Contain("parse"),
            "the failure must name the front-end stage that rejected the source");
        Assert.That(ex.Message, Does.Contain("End Class"),
            "and must carry the compiler's own diagnostic, not just 'it failed'");
    }

    /// <summary>
    /// The specific shape that made this necessary: a parse failure leaves an EMPTY program,
    /// so without the guard the helper returns a module with no functions at all and every
    /// assertion in the fixture becomes vacuous.
    /// </summary>
    [Test]
    public void BuildModule_DoesNotSilentlyReturnAnEmptyModule()
    {
        Assert.That(() => JsTestSupport.BuildModule(Unparseable),
            Throws.Exception,
            "returning an empty module here would let every JS fixture pass vacuously");
    }

    [Test]
    public void BuildModule_StillAcceptsValidSource()
    {
        var module = JsTestSupport.BuildModule("Sub Main()\nConsole.WriteLine(\"x\")\nEnd Sub");

        Assert.That(module.Functions, Is.Not.Empty);
    }
}
