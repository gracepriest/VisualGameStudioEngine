using NUnit.Framework;
using VisualGameStudio.Editor.TextMarkers;

namespace VisualGameStudio.Tests.Editor;

/// <summary>
/// Tests for the pure, headless color-value detector extracted from
/// <c>InlineColorRenderer</c>. Pins the language gate (extension classification via
/// the canonical LanguageFileTypes map) and the exact replace-range semantics the
/// renderer's click-to-pick rewrite depends on:
/// RgbCall = first R digit through the closing paren inclusive;
/// VbHex = the &amp;H start through the literal end.
/// </summary>
[TestFixture]
public class ColorMatchFinderTests
{
    // ---------------------------------------------------------------
    // ClassifyFile — must agree with LanguageFileTypes, not a third map
    // ---------------------------------------------------------------

    [TestCase("Game.bas")]
    [TestCase("Game.bl")]
    [TestCase("Game.mod")]
    [TestCase("Game.cls")]
    [TestCase("Game.class")]
    public void Classify_BasicLangExtensions_AreBasicLang(string fileName)
    {
        Assert.That(ColorMatchFinder.ClassifyFile(fileName),
            Is.EqualTo(ColorLanguage.BasicLang));
    }

    [TestCase("main.cpp")]
    [TestCase("main.cc")]
    [TestCase("main.cxx")]
    [TestCase("main.h")]
    [TestCase("main.hpp")]
    [TestCase("main.hh")]
    [TestCase("main.hxx")]
    [TestCase("main.inl")]
    public void Classify_CppExtensions_AreCpp(string fileName)
    {
        Assert.That(ColorMatchFinder.ClassifyFile(fileName),
            Is.EqualTo(ColorLanguage.Cpp));
    }

    [TestCase("notes.txt")]
    [TestCase("settings.json")]
    [TestCase(null)]
    public void Classify_OtherOrNull_AreNone(string? fileName)
    {
        Assert.That(ColorMatchFinder.ClassifyFile(fileName),
            Is.EqualTo(ColorLanguage.None));
    }

    // ---------------------------------------------------------------
    // BasicLang — RgbCall pattern
    // ---------------------------------------------------------------

    [Test]
    public void Bas_WhitelistedCall_RgbTail_Matches()
    {
        const string line = "ClearBackground(10, 20, 30)";

        var matches = ColorMatchFinder.FindMatches(line, ColorLanguage.BasicLang);

        Assert.That(matches, Has.Count.EqualTo(1));
        var m = matches[0];
        Assert.That(m.Kind, Is.EqualTo(ColorMatchKind.RgbCall));
        Assert.That(m.R, Is.EqualTo(10));
        Assert.That(m.G, Is.EqualTo(20));
        Assert.That(m.B, Is.EqualTo(30));
        Assert.That(m.A, Is.EqualTo(255));
        Assert.That(m.HasAlphaComponent, Is.False);

        // Replace range: first R digit through the closing paren INCLUSIVE.
        var expectedStart = line.IndexOf("10");
        Assert.That(m.ReplaceStart, Is.EqualTo(expectedStart));
        Assert.That(m.ReplaceLength, Is.EqualTo(line.IndexOf(')') - expectedStart + 1));
    }

    [Test]
    public void Bas_WhitelistedCall_RgbaTail_Matches()
    {
        const string line = "SetColor(10, 20, 30, 40)";

        var matches = ColorMatchFinder.FindMatches(line, ColorLanguage.BasicLang);

        Assert.That(matches, Has.Count.EqualTo(1));
        var m = matches[0];
        Assert.That(m.Kind, Is.EqualTo(ColorMatchKind.RgbCall));
        Assert.That(m.R, Is.EqualTo(10));
        Assert.That(m.G, Is.EqualTo(20));
        Assert.That(m.B, Is.EqualTo(30));
        Assert.That(m.A, Is.EqualTo(40));
        Assert.That(m.HasAlphaComponent, Is.True);

        var expectedStart = line.IndexOf("10");
        Assert.That(m.ReplaceStart, Is.EqualTo(expectedStart));
        Assert.That(m.ReplaceLength, Is.EqualTo(line.IndexOf(')') - expectedStart + 1));
    }

    [Test]
    public void Bas_ComponentOver255_NoMatch()
    {
        var matches = ColorMatchFinder.FindMatches(
            "ClearBackground(300, 20, 30)", ColorLanguage.BasicLang);

        Assert.That(matches, Is.Empty);
    }

    [Test]
    public void Bas_NonWhitelistedName_NoMatch()
    {
        var matches = ColorMatchFinder.FindMatches(
            "Foo(1,2,3)", ColorLanguage.BasicLang);

        Assert.That(matches, Is.Empty);
    }

    // ---------------------------------------------------------------
    // BasicLang — VbHex pattern
    // ---------------------------------------------------------------

    [Test]
    public void Bas_VbHex_SixDigits_Matches()
    {
        const string line = "Dim c = &H33AAFF";

        var matches = ColorMatchFinder.FindMatches(line, ColorLanguage.BasicLang);

        Assert.That(matches, Has.Count.EqualTo(1));
        var m = matches[0];
        Assert.That(m.Kind, Is.EqualTo(ColorMatchKind.VbHex));
        Assert.That(m.R, Is.EqualTo(0x33));
        Assert.That(m.G, Is.EqualTo(0xAA));
        Assert.That(m.B, Is.EqualTo(0xFF));
        Assert.That(m.A, Is.EqualTo(255));
        Assert.That(m.HasAlphaComponent, Is.False);

        // Replace range: the &H start through the literal end.
        Assert.That(m.ReplaceStart, Is.EqualTo(line.IndexOf("&H")));
        Assert.That(m.ReplaceLength, Is.EqualTo("&H33AAFF".Length));
    }

    [Test]
    public void Bas_VbHex_EightDigits_Matches()
    {
        const string line = "Dim c = &H8033AAFF";

        var matches = ColorMatchFinder.FindMatches(line, ColorLanguage.BasicLang);

        Assert.That(matches, Has.Count.EqualTo(1));
        var m = matches[0];
        Assert.That(m.Kind, Is.EqualTo(ColorMatchKind.VbHex));
        Assert.That(m.A, Is.EqualTo(0x80));
        Assert.That(m.R, Is.EqualTo(0x33));
        Assert.That(m.G, Is.EqualTo(0xAA));
        Assert.That(m.B, Is.EqualTo(0xFF));
        Assert.That(m.HasAlphaComponent, Is.True);

        Assert.That(m.ReplaceStart, Is.EqualTo(line.IndexOf("&H")));
        Assert.That(m.ReplaceLength, Is.EqualTo("&H8033AAFF".Length));
    }

    // ---------------------------------------------------------------
    // Language gate — Cpp gets NO patterns in this task, None never matches
    // ---------------------------------------------------------------

    [TestCase("ClearBackground(10, 20, 30)")]
    [TestCase("SetColor(10, 20, 30, 40)")]
    [TestCase("Dim c = &H33AAFF")]
    [TestCase("DrawRectangle(10, 20, 100, 50)")] // raylib geometry call — the latent false positive
    public void Cpp_AnyPattern_NoMatch(string line)
    {
        var matches = ColorMatchFinder.FindMatches(line, ColorLanguage.Cpp);

        Assert.That(matches, Is.Empty);
    }

    [TestCase("ClearBackground(10, 20, 30)")]
    [TestCase("SetColor(10, 20, 30, 40)")]
    [TestCase("Dim c = &H33AAFF")]
    public void None_NeverMatches(string line)
    {
        var matches = ColorMatchFinder.FindMatches(line, ColorLanguage.None);

        Assert.That(matches, Is.Empty);
    }
}
