using NUnit.Framework;
using BasicLang.Compiler.Driver;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 4: `--target=javascript` reaches the JavaScript backend and writes `.js`.
///
/// <para>Exercises the CLI's OWN dispatch helpers rather than a BackendRegistry lookup,
/// because the CLI does not go through the registry: it has a separate switch in
/// <c>Program.GenerateCode</c> / <c>Program.GetOutputExtension</c>. A registry-only test
/// would pass while the CLI silently emitted C#, which is exactly the two-entry-point
/// drift CLAUDE.md warns about.</para>
/// </summary>
[TestFixture]
public class JavaScriptCliTargetTests
{
    [TestCase("javascript")]
    [TestCase("js")]
    [TestCase("JavaScript")]
    public void GenerateCode_JavaScriptTarget_EmitsJavaScript(string target)
    {
        var ir = JsTestSupport.BuildModule("Sub Main()\nConsole.WriteLine(\"Hello\")\nEnd Sub");

        var output = Program.GenerateCode(ir, target);

        Assert.That(output, Does.Contain("console.log"));
        Assert.That(output, Does.Not.Contain("namespace"), "must not have fallen through to C#");
    }

    [TestCase("javascript", ".js")]
    [TestCase("js", ".js")]
    [TestCase("cpp", ".cpp")]
    [TestCase("csharp", ".cs")]
    public void GetOutputExtension_MapsTarget(string target, string expected)
    {
        Assert.That(Program.GetOutputExtension(target), Is.EqualTo(expected));
    }

    /// <summary>
    /// An unrecognised --target used to fall through to C# silently, so `--target=jscript`
    /// wrote a .cs file and left the user wondering why their web page was empty. A typo
    /// must be an error, not a different language.
    /// </summary>
    [Test]
    public void GenerateCode_UnknownTarget_Throws()
    {
        var ir = JsTestSupport.BuildModule("Sub Main()\nEnd Sub");

        var ex = Assert.Throws<System.NotSupportedException>(
            () => Program.GenerateCode(ir, "jscript"));

        Assert.That(ex!.Message, Does.Contain("jscript"));
        Assert.That(ex.Message, Does.Contain("javascript"), "the error should list the valid targets");
    }

    [Test]
    public void GetOutputExtension_UnknownTarget_Throws()
    {
        Assert.Throws<System.NotSupportedException>(() => Program.GetOutputExtension("jscript"));
    }

    /// <summary>
    /// An absent target keeps defaulting to C#. That is the documented CLI default and
    /// callers rely on it; only a NON-EMPTY unrecognised value is an error.
    /// </summary>
    [Test]
    public void GenerateCode_NullTarget_StillDefaultsToCSharp()
    {
        var ir = JsTestSupport.BuildModule("Sub Main()\nEnd Sub");

        Assert.DoesNotThrow(() => Program.GenerateCode(ir, null));
        Assert.That(Program.GetOutputExtension(null), Is.EqualTo(".cs"));
    }
}
