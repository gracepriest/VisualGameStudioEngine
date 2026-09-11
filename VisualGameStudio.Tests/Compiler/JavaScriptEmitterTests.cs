using System.IO;
using BasicLang.Compiler.CodeGen.JavaScript;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 25 — the emitter that turns generated JavaScript into a runnable web page.
///
/// <para>Until this task the backend produced a STRING and every caller wrote it out by hand:
/// <c>ICodeGenerator.Generate</c> returns one string, and there is no IEmitter, no
/// EmitToDirectory and no base class anywhere in the repo. A browser needs two files, so the
/// single-string contract had to be widened somewhere — here, rather than in the interface
/// every other backend implements.</para>
/// </summary>
[TestFixture]
public class JavaScriptEmitterTests
{
    private string _dir;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "BasicLang_JsEmit_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* a locked temp dir must not fail the test that already passed */ }
    }

    private string Read(string name) => File.ReadAllText(Path.Combine(_dir, name));

    [Test]
    public void Emit_WritesTheScript()
    {
        JavaScriptEmitter.Emit(_dir, "app.js", "console.log(1);");

        Assert.That(Read("app.js"), Is.EqualTo("console.log(1);"));
    }

    [Test]
    public void Emit_WritesAnIndexHtmlHarness()
    {
        JavaScriptEmitter.Emit(_dir, "app.js", "console.log(1);");

        Assert.That(Read("index.html"),
            Does.Contain("<script type=\"module\" src=\"app.js\"></script>"));
    }

    /// <summary>
    /// THE rule the plan calls out. The single-file CLI route writes its output NEXT TO THE
    /// SOURCE FILE, which is exactly where a hand-written index.html lives — so this is
    /// load-bearing from the first build, not a someday-nicety.
    /// </summary>
    [Test]
    public void Emit_NeverOverwritesAnExistingIndexHtml()
    {
        var mine = "<!-- hand written, do not clobber -->";
        File.WriteAllText(Path.Combine(_dir, "index.html"), mine);

        JavaScriptEmitter.Emit(_dir, "app.js", "console.log(1);");

        Assert.That(Read("index.html"), Is.EqualTo(mine));
    }

    /// <summary>...but the SCRIPT is always rewritten; it is build output.</summary>
    [Test]
    public void Emit_AlwaysOverwritesTheScript()
    {
        File.WriteAllText(Path.Combine(_dir, "app.js"), "stale");

        JavaScriptEmitter.Emit(_dir, "app.js", "fresh");

        Assert.That(Read("app.js"), Is.EqualTo("fresh"));
    }

    /// <summary>
    /// The project route names its output after the assembly, so the harness cannot hardcode
    /// "app.js" — a MyGame.blproj emits MyGame.js and the page must load THAT.
    /// </summary>
    [Test]
    public void Emit_HarnessReferencesTheActualScriptName()
    {
        JavaScriptEmitter.Emit(_dir, "MyGame.js", "console.log(1);");

        var html = Read("index.html");
        Assert.That(html, Does.Contain("src=\"MyGame.js\""));
        Assert.That(html, Does.Not.Contain("app.js"));
    }

    [Test]
    public void Emit_ReturnsEveryPathItWrote()
    {
        var written = JavaScriptEmitter.Emit(_dir, "app.js", "console.log(1);");

        Assert.That(written, Is.EquivalentTo(new[]
        {
            Path.Combine(_dir, "app.js"),
            Path.Combine(_dir, "index.html"),
        }));
    }

    /// <summary>A skipped index.html must not be reported as written.</summary>
    [Test]
    public void Emit_DoesNotReportASkippedHarness()
    {
        File.WriteAllText(Path.Combine(_dir, "index.html"), "mine");

        var written = JavaScriptEmitter.Emit(_dir, "app.js", "console.log(1);");

        Assert.That(written, Is.EquivalentTo(new[] { Path.Combine(_dir, "app.js") }));
    }

    [Test]
    public void Emit_CreatesTheDirectoryWhenAbsent()
    {
        var nested = Path.Combine(_dir, "bin", "Release");

        JavaScriptEmitter.Emit(nested, "app.js", "console.log(1);");

        Assert.That(File.Exists(Path.Combine(nested, "app.js")), Is.True);
    }

    /// <summary>
    /// The harness must be a real document, not a bare script tag — a browser served a
    /// fragment still runs it, but devtools and the source-map UI both key off the document.
    /// </summary>
    [Test]
    public void Emit_HarnessIsAWellFormedDocument()
    {
        JavaScriptEmitter.Emit(_dir, "app.js", "console.log(1);");

        var html = Read("index.html");
        Assert.That(html, Does.StartWith("<!DOCTYPE html>"));
        Assert.That(html, Does.Contain("<html"));
        Assert.That(html, Does.Contain("charset=\"utf-8\""));
        Assert.That(html, Does.Contain("</html>"));
    }

    /// <summary>
    /// A project name reaches the title, so it must be HTML-escaped. `&amp;` and `&lt;` in an
    /// assembly name are legal on disk and would otherwise break the document.
    /// </summary>
    /// <summary>
    /// The title is TEXT and is HTML-escaped; the src is a URL and is PERCENT-encoded.
    /// The two are different escapings and this test used to conflate them — it asserted
    /// <c>src="a&amp;amp;b.js"</c>, which is well-formed HTML naming the file <c>a&amp;b.js</c>
    /// and happens to work, so it could not tell a correct URL from a broken one. Percent
    /// encoding is the strictly stronger claim: it is also correct for <c>a&amp;b.js</c>, and
    /// it is the only thing that saves the names below.
    /// </summary>
    [Test]
    public void Emit_PercentEncodesTheScriptNameAndHtmlEscapesTheTitle()
    {
        JavaScriptEmitter.Emit(_dir, "a&b.js", "console.log(1);", title: "<Tom & Jerry>");

        var html = Read("index.html");
        Assert.That(html, Does.Contain("src=\"a%26b.js\""));
        Assert.That(html, Does.Contain("&lt;Tom &amp; Jerry&gt;"));
        Assert.That(html, Does.Not.Contain("<Tom"));
    }

    /// <summary>
    /// MEASURED, not theorised: a project named "Site #1" builds clean (exit 0), writes
    /// "Site #1.js" beside an index.html whose src is the RAW name, and then the browser
    /// splits that src at the '#' — it requested <c>/Site%20</c>, got a 404, and rendered a
    /// blank page with the build still reporting success. '#' is a legal file-name character
    /// on every platform this targets and <c>IsValidProjectName</c> admits it, so File → New
    /// Project can produce it.
    ///
    /// <para>'%' is the same class of bug from the other side: the server percent-DECODES the
    /// request path, so a raw '%' in the name either mangles or throws on the way back.</para>
    /// </summary>
    [TestCase("Site #1.js", "Site%20%231.js")]
    [TestCase("50% Off.js", "50%25%20Off.js")]
    [TestCase("a b.js", "a%20b.js")]
    public void Emit_HarnessSrcSurvivesAUrlSignificantCharacterInTheName(string script, string expectedSrc)
    {
        JavaScriptEmitter.Emit(_dir, script, "console.log(1);");

        var html = Read("index.html");
        Assert.That(html, Does.Contain($"src=\"{expectedSrc}\""));
        // The file on disk keeps its real name — only the URL is encoded.
        Assert.That(File.Exists(Path.Combine(_dir, script)), Is.True);
    }

    /// <summary>An ordinary name must be byte-identical to before — encoding is a no-op on it.</summary>
    [Test]
    public void Emit_LeavesAnOrdinaryScriptNameUnencoded()
    {
        JavaScriptEmitter.Emit(_dir, "MyGame.js", "console.log(1);");

        Assert.That(Read("index.html"), Does.Contain("src=\"MyGame.js\""));
    }

    // ---------------------------------------------------------------- source map (task 26)

    /// <summary>
    /// The emitter takes the map as an OPTIONAL argument so task 26 slots in without
    /// reshaping the call sites. No map means no .map file and no trailing comment — an
    /// unconditional `//# sourceMappingURL=` pointing at a file that does not exist makes
    /// devtools log a 404 on every page load.
    /// </summary>
    [Test]
    public void Emit_WithoutASourceMap_WritesNoMapAndNoComment()
    {
        JavaScriptEmitter.Emit(_dir, "app.js", "console.log(1);");

        Assert.That(File.Exists(Path.Combine(_dir, "app.js.map")), Is.False);
        Assert.That(Read("app.js"), Does.Not.Contain("sourceMappingURL"));
    }

    [Test]
    public void Emit_WithASourceMap_WritesItAndAppendsTheComment()
    {
        JavaScriptEmitter.Emit(_dir, "app.js", "console.log(1);", sourceMapJson: "{\"version\":3}");

        Assert.That(Read("app.js.map"), Is.EqualTo("{\"version\":3}"));
        Assert.That(Read("app.js").TrimEnd(), Does.EndWith("//# sourceMappingURL=app.js.map"));
    }

    /// <summary>
    /// sourceMappingURL is a URL too, resolved by devtools against the script's own URL — so
    /// it needs the same percent-encoding as the src attribute, or a '#' in the name silently
    /// costs the user their BasicLang source view while the build still succeeds.
    /// </summary>
    [Test]
    public void Emit_PercentEncodesTheSourceMappingUrl()
    {
        JavaScriptEmitter.Emit(_dir, "Site #1.js", "console.log(1);", sourceMapJson: "{\"version\":3}");

        Assert.That(File.Exists(Path.Combine(_dir, "Site #1.js.map")), Is.True, "the map keeps its real file name");
        Assert.That(Read("Site #1.js").TrimEnd(),
            Does.EndWith("//# sourceMappingURL=Site%20%231.js.map"));
    }
}
