using NUnit.Framework;
using VisualGameStudio.Editor.TextMarkers;

namespace VisualGameStudio.Tests.Editor;

/// <summary>
/// Tests for the pure, headless color-value detector extracted from
/// <c>InlineColorRenderer</c>. Pins the language gate (extension classification via
/// the canonical LanguageFileTypes map) and the exact replace-range semantics the
/// renderer's click-to-pick rewrite depends on:
/// RgbCall = first R digit through the closing paren inclusive;
/// VbHex = the &amp;H start through the literal end;
/// CppHex = the 0x start through the literal end (exactly 6 or 8 hex digits,
/// word-boundary bounded — mirrors VbHex but 0x is Cpp-only, &amp;H stays
/// BasicLang-only).
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
    // Cpp — CppHex pattern (0x literals, mirrors VbHex but Cpp-only)
    // ---------------------------------------------------------------

    [Test]
    public void Cpp_CppHex_SixDigits_Matches()
    {
        const string line = "auto c = 0x33AAFF;";

        var matches = ColorMatchFinder.FindMatches(line, ColorLanguage.Cpp);

        Assert.That(matches, Has.Count.EqualTo(1));
        var m = matches[0];
        Assert.That(m.Kind, Is.EqualTo(ColorMatchKind.CppHex));
        Assert.That(m.R, Is.EqualTo(0x33));
        Assert.That(m.G, Is.EqualTo(0xAA));
        Assert.That(m.B, Is.EqualTo(0xFF));
        Assert.That(m.A, Is.EqualTo(255));
        Assert.That(m.HasAlphaComponent, Is.False);

        // Replace range: the 0x start through the literal end.
        Assert.That(m.ReplaceStart, Is.EqualTo(line.IndexOf("0x")));
        Assert.That(m.ReplaceLength, Is.EqualTo("0x33AAFF".Length));
    }

    [Test]
    public void Cpp_CppHex_EightDigits_Matches()
    {
        const string line = "auto c = 0x8033AAFF;";

        var matches = ColorMatchFinder.FindMatches(line, ColorLanguage.Cpp);

        Assert.That(matches, Has.Count.EqualTo(1));
        var m = matches[0];
        Assert.That(m.Kind, Is.EqualTo(ColorMatchKind.CppHex));
        Assert.That(m.A, Is.EqualTo(0x80));
        Assert.That(m.R, Is.EqualTo(0x33));
        Assert.That(m.G, Is.EqualTo(0xAA));
        Assert.That(m.B, Is.EqualTo(0xFF));
        Assert.That(m.HasAlphaComponent, Is.True);

        Assert.That(m.ReplaceStart, Is.EqualTo(line.IndexOf("0x")));
        Assert.That(m.ReplaceLength, Is.EqualTo("0x8033AAFF".Length));
    }

    [Test]
    public void Bas_CppHex_NoMatch()
    {
        // 0x is C++-only syntax — &H stays .bas-only, 0x never lights up there.
        var matches = ColorMatchFinder.FindMatches(
            "auto c = 0x33AAFF;", ColorLanguage.BasicLang);

        Assert.That(matches, Is.Empty);
    }

    [TestCase("auto c = 0xFFF;")]
    [TestCase("auto c = 0xFFFFFFFFF;")]
    [TestCase("auto c = 0x8033AAF;")] // 7 digits — the {8}|{6} exact alternation must reject this near-miss
    public void Cpp_CppHex_ShortOrLongHex_NoMatch(string line)
    {
        // Exactly 6 or 8 hex digits only — not 7 (short) and not 9+ (long).
        var matches = ColorMatchFinder.FindMatches(line, ColorLanguage.Cpp);

        Assert.That(matches, Is.Empty);
    }

    [TestCase("g0x33AAFF")]
    [TestCase("00x33AAFF")]
    [TestCase("myColor0x8033AAFF")]
    public void Cpp_CppHex_InsideIdentifier_NoMatch(string line)
    {
        // 0 and x are both word characters, so without a leading boundary guard
        // "0x..." false-matches mid-identifier (e.g. g0x33AAFF) — corrupting a
        // variable name if the click-to-pick rewrite fires on it.
        var matches = ColorMatchFinder.FindMatches(line, ColorLanguage.Cpp);

        Assert.That(matches, Is.Empty);
    }

    // ---------------------------------------------------------------
    // Framework_ prefix rules (raw framework.h exports)
    // BasicLang: prefix OPTIONAL — wrapper names AND raw exports light up.
    // Cpp: prefix REQUIRED — unprefixed names would false-positive on
    // raylib's own DrawRectangle/DrawCircle geometry overloads.
    // ---------------------------------------------------------------

    [Test]
    public void Bas_FrameworkPrefixedCall_Matches()
    {
        const string line = "Framework_ClearBackground(10, 10, 25, 255)";

        var matches = ColorMatchFinder.FindMatches(line, ColorLanguage.BasicLang);

        Assert.That(matches, Has.Count.EqualTo(1));
        var m = matches[0];
        Assert.That(m.Kind, Is.EqualTo(ColorMatchKind.RgbCall));
        Assert.That(m.R, Is.EqualTo(10));
        Assert.That(m.G, Is.EqualTo(10));
        Assert.That(m.B, Is.EqualTo(25));
        Assert.That(m.A, Is.EqualTo(255));
        Assert.That(m.HasAlphaComponent, Is.True);

        // Replace range: first R digit through the closing paren inclusive —
        // the function name (prefix included) stays outside the rewrite.
        var expectedStart = line.IndexOf("10");
        Assert.That(m.ReplaceStart, Is.EqualTo(expectedStart));
        Assert.That(m.ReplaceLength, Is.EqualTo(line.IndexOf(')') - expectedStart + 1));
    }

    [Test]
    public void Cpp_FrameworkPrefixedCall_Matches()
    {
        const string line = "Framework_ClearBackground(10, 10, 25, 255);";

        var matches = ColorMatchFinder.FindMatches(line, ColorLanguage.Cpp);

        Assert.That(matches, Has.Count.EqualTo(1));
        var m = matches[0];
        Assert.That(m.Kind, Is.EqualTo(ColorMatchKind.RgbCall));
        Assert.That(m.R, Is.EqualTo(10));
        Assert.That(m.G, Is.EqualTo(10));
        Assert.That(m.B, Is.EqualTo(25));
        Assert.That(m.A, Is.EqualTo(255));
        Assert.That(m.HasAlphaComponent, Is.True);

        var expectedStart = line.IndexOf("10");
        Assert.That(m.ReplaceStart, Is.EqualTo(expectedStart));
        Assert.That(m.ReplaceLength, Is.EqualTo(line.IndexOf(')') - expectedStart + 1));
    }

    [Test]
    public void Cpp_UnprefixedWhitelistName_StillNoMatch()
    {
        var matches = ColorMatchFinder.FindMatches(
            "ClearBackground(1,2,3)", ColorLanguage.Cpp);

        Assert.That(matches, Is.Empty);
    }

    [Test]
    public void Cpp_FrameworkPrefixedNonWhitelisted_NoMatch()
    {
        // Framework_GetTime is a real export (framework.h) but takes no color.
        var matches = ColorMatchFinder.FindMatches(
            "Framework_GetTime(1,2,3)", ColorLanguage.Cpp);

        Assert.That(matches, Is.Empty);
    }

    // ---------------------------------------------------------------
    // Audit-added whitelist names (framework.h color-tail exports)
    // ---------------------------------------------------------------

    [Test]
    public void Bas_AuditAddedExport_Matches()
    {
        // Ecs_SetSpriteTint(entity, r, g, b, a) — added by the framework.h audit.
        const string line = "Ecs_SetSpriteTint(7, 200, 100, 50, 255)";

        var matches = ColorMatchFinder.FindMatches(line, ColorLanguage.BasicLang);

        Assert.That(matches, Has.Count.EqualTo(1));
        var m = matches[0];
        Assert.That(m.Kind, Is.EqualTo(ColorMatchKind.RgbCall));
        Assert.That(m.R, Is.EqualTo(200));
        Assert.That(m.G, Is.EqualTo(100));
        Assert.That(m.B, Is.EqualTo(50));
        Assert.That(m.A, Is.EqualTo(255));
        Assert.That(m.HasAlphaComponent, Is.True);

        // Replace range starts at the R component, not the leading entity arg.
        Assert.That(m.ReplaceStart, Is.EqualTo(line.IndexOf("200")));
    }

    [Test]
    public void Cpp_AuditAddedExport_PrefixedMatches()
    {
        const string line = "Framework_Ecs_SetSpriteTint(7, 200, 100, 50, 255);";

        var matches = ColorMatchFinder.FindMatches(line, ColorLanguage.Cpp);

        Assert.That(matches, Has.Count.EqualTo(1));
        var m = matches[0];
        Assert.That(m.Kind, Is.EqualTo(ColorMatchKind.RgbCall));
        Assert.That(m.R, Is.EqualTo(200));
        Assert.That(m.G, Is.EqualTo(100));
        Assert.That(m.B, Is.EqualTo(50));
        Assert.That(m.A, Is.EqualTo(255));
    }

    // Light_SetColor(lightId, r, g, b) and Path_DrawDebug(pathId, r, g, b) have
    // EXACTLY-3-component color tails preceded by a numeric id. The greedy
    // 4-capture window would absorb the id as R (Light_SetColor(2, 255, 128, 0)
    // -> R=2!) and hand the id slot to the click-to-pick rewriter — so both are
    // excluded from the whitelist and must never match.
    [TestCase("Light_SetColor(2, 255, 128, 0)")]
    [TestCase("Path_DrawDebug(3, 255, 128, 0)")]
    public void Bas_LightSetColor_IdAbsorption_NoMatch(string line)
    {
        var matches = ColorMatchFinder.FindMatches(line, ColorLanguage.BasicLang);

        Assert.That(matches, Is.Empty);
    }

    // ---------------------------------------------------------------
    // Language gate — Cpp matches ONLY Framework_-prefixed RgbCalls
    // (no VbHex, no unprefixed names); None never matches
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

    // ---------------------------------------------------------------
    // Cpp — BraceInit pattern (raylib Color{} brace literals, Cpp-only).
    // A Color/(Color)/CLITERAL(Color) prefix is REQUIRED; the replace
    // range is the brace group ONLY — the prefix survives the rewrite.
    // ---------------------------------------------------------------

    [Test]
    public void Cpp_BraceInit_ColorPrefix_Matches()
    {
        const string line = "Color{255, 0, 0, 255}";

        var matches = ColorMatchFinder.FindMatches(line, ColorLanguage.Cpp);

        Assert.That(matches, Has.Count.EqualTo(1));
        var m = matches[0];
        Assert.That(m.Kind, Is.EqualTo(ColorMatchKind.BraceInit));
        Assert.That(m.R, Is.EqualTo(255));
        Assert.That(m.G, Is.EqualTo(0));
        Assert.That(m.B, Is.EqualTo(0));
        Assert.That(m.A, Is.EqualTo(255));
        Assert.That(m.HasAlphaComponent, Is.True);

        // Replace range: AT the opening brace (not the 'C' of Color), through
        // the closing brace inclusive.
        var expectedStart = line.IndexOf('{');
        Assert.That(m.ReplaceStart, Is.EqualTo(expectedStart));
        Assert.That(m.ReplaceLength, Is.EqualTo(line.IndexOf('}') - expectedStart + 1));
    }

    [Test]
    public void Cpp_BraceInit_ParenColorPrefix_Matches()
    {
        const string line = "(Color){255, 0, 0}";

        var matches = ColorMatchFinder.FindMatches(line, ColorLanguage.Cpp);

        Assert.That(matches, Has.Count.EqualTo(1));
        var m = matches[0];
        Assert.That(m.Kind, Is.EqualTo(ColorMatchKind.BraceInit));
        Assert.That(m.R, Is.EqualTo(255));
        Assert.That(m.G, Is.EqualTo(0));
        Assert.That(m.B, Is.EqualTo(0));
        Assert.That(m.HasAlphaComponent, Is.False);

        var expectedStart = line.IndexOf('{');
        Assert.That(m.ReplaceStart, Is.EqualTo(expectedStart));
        Assert.That(m.ReplaceLength, Is.EqualTo(line.IndexOf('}') - expectedStart + 1));
    }

    [Test]
    public void Cpp_BraceInit_CLiteralPrefix_Matches()
    {
        const string line = "CLITERAL(Color){ 255, 0, 0, 255 }";

        var matches = ColorMatchFinder.FindMatches(line, ColorLanguage.Cpp);

        Assert.That(matches, Has.Count.EqualTo(1));
        var m = matches[0];
        Assert.That(m.Kind, Is.EqualTo(ColorMatchKind.BraceInit));
        Assert.That(m.R, Is.EqualTo(255));
        Assert.That(m.G, Is.EqualTo(0));
        Assert.That(m.B, Is.EqualTo(0));
        Assert.That(m.A, Is.EqualTo(255));

        var expectedStart = line.IndexOf('{');
        Assert.That(m.ReplaceStart, Is.EqualTo(expectedStart));
        Assert.That(m.ReplaceLength, Is.EqualTo(line.IndexOf('}') - expectedStart + 1));
    }

    [TestCase("MYCLITERAL(Color){10, 20, 30}")]
    [TestCase("XCLITERAL(Color){1,2,3}")]
    public void Cpp_BraceInit_CLiteralInsideIdentifier_NoMatch(string line)
    {
        // Neither the CLITERAL(...) nor the bare (Color) alternative in
        // BraceInitColorPattern originally had a leading boundary guard, so
        // "CLITERAL(Color){...}" false-matched mid-identifier (e.g. MYCLITERAL(...))
        // — symmetry with the \bColor\b alternative, which already requires one.
        // Guarding CLITERAL alone is not enough: (Color) is a literal substring of
        // CLITERAL(Color), so the bare-paren alternative needs its own guard too.
        var matches = ColorMatchFinder.FindMatches(line, ColorLanguage.Cpp);

        Assert.That(matches, Is.Empty);
    }

    [Test]
    public void Cpp_BareBraces_NoMatch()
    {
        // No Color/(Color)/CLITERAL(Color) prefix — must never match.
        var matches = ColorMatchFinder.FindMatches(
            "{255, 0, 0, 255}", ColorLanguage.Cpp);

        Assert.That(matches, Is.Empty);
    }

    [Test]
    public void Bas_BraceInit_NoMatch()
    {
        // BraceInit is Cpp-only.
        var matches = ColorMatchFinder.FindMatches(
            "Color{255, 0, 0}", ColorLanguage.BasicLang);

        Assert.That(matches, Is.Empty);
    }

    [Test]
    public void Cpp_BraceInit_ComponentOver255_NoMatch()
    {
        var matches = ColorMatchFinder.FindMatches(
            "Color{300, 0, 0}", ColorLanguage.Cpp);

        Assert.That(matches, Is.Empty);
    }
}
