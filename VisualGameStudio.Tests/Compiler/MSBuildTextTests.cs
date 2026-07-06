using NUnit.Framework;
using BasicLang.Compiler.ProjectSystem;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// MSBuildText.EscapeValue protects user-controlled strings (project names,
/// reference names, hint paths) embedded into generated .csproj files.
/// Two layers stack: MSBuild %XX escapes (';' is the item-list separator that
/// caused MSB4094 for a project named ";k;lk;lkl;k;l") and XML entities
/// ('&amp;' etc. — a raw '&amp;' fails the csproj XML parse with MSB4025).
/// The XML parser decodes entities first, then MSBuild unescapes %XX, so the
/// original string round-trips to the build tasks.
/// </summary>
[TestFixture]
public class MSBuildTextTests
{
    [Test]
    public void EscapeValue_Semicolons_BecomeMsBuildEscapes()
    {
        // The reported repro: ';' in the name splits derived item paths.
        Assert.That(MSBuildText.EscapeValue(";k;lk;lkl;k;l"),
            Is.EqualTo("%3Bk%3Blk%3Blkl%3Bk%3Bl"));
    }

    [Test]
    public void EscapeValue_XmlSpecials_BecomeEntities()
    {
        Assert.That(MSBuildText.EscapeValue("Tom & Jerry"), Is.EqualTo("Tom &amp; Jerry"));
        Assert.That(MSBuildText.EscapeValue("a<b>c\"d"), Is.EqualTo("a&lt;b&gt;c&quot;d"));
    }

    [Test]
    public void EscapeValue_Percent_IsEscapedWithoutDoubleEscaping()
    {
        // '%' introduces MSBuild escape sequences, so it must itself be escaped —
        // and exactly once (single pass): an input that already looks like an
        // escape sequence is preserved literally.
        Assert.That(MSBuildText.EscapeValue("100%"), Is.EqualTo("100%25"));
        Assert.That(MSBuildText.EscapeValue("%3B"), Is.EqualTo("%253B"));
    }

    [Test]
    public void EscapeValue_MsBuildExpressionAndWildcardChars_AreEscaped()
    {
        Assert.That(MSBuildText.EscapeValue("$(Evil)"), Is.EqualTo("%24%28Evil%29"));
        Assert.That(MSBuildText.EscapeValue("@(Items)"), Is.EqualTo("%40%28Items%29"));
        Assert.That(MSBuildText.EscapeValue("O'Brien"), Is.EqualTo("O%27Brien"));
        Assert.That(MSBuildText.EscapeValue("a*?.cs"), Is.EqualTo("a%2A%3F.cs"));
    }

    [Test]
    public void EscapeValue_CombinedHostileName_EscapesBothLayers()
    {
        // The e2e pipeline test's project name.
        Assert.That(MSBuildText.EscapeValue("A;B & C's 100%"),
            Is.EqualTo("A%3BB &amp; C%27s 100%25"));
    }

    [Test]
    public void EscapeValue_OrdinaryNamesAndPaths_PassThroughUnchanged()
    {
        Assert.That(MSBuildText.EscapeValue("MyGame_2.Final-v3 beta"),
            Is.EqualTo("MyGame_2.Final-v3 beta"));
        Assert.That(MSBuildText.EscapeValue(@"C:\Users\me\RaylibWrapper.dll"),
            Is.EqualTo(@"C:\Users\me\RaylibWrapper.dll"));
    }

    [Test]
    public void EscapeValue_NullOrEmpty_ReturnsEmpty()
    {
        Assert.That(MSBuildText.EscapeValue(null), Is.EqualTo(string.Empty));
        Assert.That(MSBuildText.EscapeValue(string.Empty), Is.EqualTo(string.Empty));
    }

    // ------------------------------------------------------------------
    // FindSpecialCharacters — powers the New Project dialog's warn-but-allow
    // name hint. Must flag EXACTLY the characters EscapeValue escapes, so the
    // warning can never drift from what the build layer actually handles.
    // ------------------------------------------------------------------

    [Test]
    public void FindSpecialCharacters_ReturnsDistinctSpecialsInFirstAppearanceOrder()
    {
        Assert.That(MSBuildText.FindSpecialCharacters("A;B & C's 100%"), Is.EqualTo(";&'%"));
        Assert.That(MSBuildText.FindSpecialCharacters(";k;lk;lkl;k;l"), Is.EqualTo(";"));
    }

    [Test]
    public void FindSpecialCharacters_OrdinaryNames_ReturnEmpty()
    {
        Assert.That(MSBuildText.FindSpecialCharacters("MyGame_2.Final-v3 beta"), Is.Empty);
        Assert.That(MSBuildText.FindSpecialCharacters(null), Is.Empty);
        Assert.That(MSBuildText.FindSpecialCharacters(string.Empty), Is.Empty);
    }

    [Test]
    public void FindSpecialCharacters_AgreesWithEscapeValue()
    {
        // The invariant that keeps the dialog warning honest: a name is flagged
        // if and only if escaping would change it.
        foreach (var name in new[] { "Plain", "a;b", "a&b", "100%", "$x", "@y", "o'k", "(z)", "w*", "q?", "<t>", "\"u\"" })
        {
            var flagged = MSBuildText.FindSpecialCharacters(name).Length > 0;
            var changed = MSBuildText.EscapeValue(name) != name;
            Assert.That(flagged, Is.EqualTo(changed), $"disagreement for '{name}'");
        }
    }
}
