using NUnit.Framework;
using VisualGameStudio.Editor.TextMarkers;

namespace VisualGameStudio.Tests.Editor;

/// <summary>
/// Tests for the pure apply-side rewriter extracted from
/// <c>CodeEditorControl.OnColorPicked</c>. The CURRENT inline behaviors are the
/// contract — ported exactly:
/// RgbCall keeps the comma-count alpha heuristic (original had &gt;= 3 commas OR
/// the picked alpha is translucent → four components) and re-emits the closing
/// paren because the replace range includes it; VbHex digit count is
/// PICKED-ALPHA-DRIVEN (a &lt; 255 → 8 digits &amp;H{A}{R}{G}{B}, else 6 digits
/// &amp;H{R}{G}{B}), uppercase X2 hex; CppHex mirrors VbHex exactly but with a
/// <c>0x</c> prefix (a &lt; 255 → 8 digits 0x{A}{R}{G}{B}, else 6 digits
/// 0x{R}{G}{B}), uppercase X2 hex; BraceInit mirrors RgbCall's comma-count
/// alpha heuristic exactly, with braces instead of parens and no prefix
/// re-emitted (the replace range is the brace group only).
/// </summary>
[TestFixture]
public class ColorTextRewriterTests
{
    // ---------------------------------------------------------------
    // RgbCall — comma-count heuristic + closing-paren re-emission
    // ---------------------------------------------------------------

    [Test]
    public void RgbCall_ThreeComponents_PickedOpaque_StaysThree()
    {
        var result = ColorTextRewriter.Rewrite(
            ColorMatchKind.RgbCall, "10, 20, 30)", 1, 2, 3, 255);
        Assert.That(result, Is.EqualTo("1, 2, 3)"));
    }

    [Test]
    public void RgbCall_ThreeComponents_PickedAlpha_BecomesFour()
    {
        var result = ColorTextRewriter.Rewrite(
            ColorMatchKind.RgbCall, "10, 20, 30)", 1, 2, 3, 128);
        Assert.That(result, Is.EqualTo("1, 2, 3, 128)"));
    }

    [Test]
    public void RgbCall_FourComponents_StaysFour()
    {
        var result = ColorTextRewriter.Rewrite(
            ColorMatchKind.RgbCall, "10, 20, 30, 255)", 1, 2, 3, 255);
        Assert.That(result, Is.EqualTo("1, 2, 3, 255)"));
    }

    // ---------------------------------------------------------------
    // VbHex — digit count is picked-alpha-driven, uppercase X2
    // ---------------------------------------------------------------

    [Test]
    public void VbHex_OpaquePick_EmitsSixDigits()
    {
        var result = ColorTextRewriter.Rewrite(
            ColorMatchKind.VbHex, "&H8033AAFF", 1, 2, 3, 255);
        Assert.That(result, Is.EqualTo("&H010203"));
    }

    [Test]
    public void VbHex_TranslucentPick_EmitsEightDigits()
    {
        var result = ColorTextRewriter.Rewrite(
            ColorMatchKind.VbHex, "&H33AAFF", 1, 2, 3, 128);
        Assert.That(result, Is.EqualTo("&H80010203"));
    }

    // ---------------------------------------------------------------
    // CppHex — mirrors VbHex exactly, but with a 0x prefix instead of &H
    // ---------------------------------------------------------------

    [Test]
    public void CppHex_OpaquePick_EmitsSixDigits()
    {
        var result = ColorTextRewriter.Rewrite(
            ColorMatchKind.CppHex, "0x8033AAFF", 1, 2, 3, 255);
        Assert.That(result, Is.EqualTo("0x010203"));
    }

    [Test]
    public void CppHex_TranslucentPick_EmitsEightDigits()
    {
        var result = ColorTextRewriter.Rewrite(
            ColorMatchKind.CppHex, "0x33AAFF", 1, 2, 3, 128);
        Assert.That(result, Is.EqualTo("0x80010203"));
    }

    // ---------------------------------------------------------------
    // BraceInit — mirrors RgbCall's comma-count alpha heuristic, but
    // braces instead of parens (no prefix — the prefix is outside the
    // replace range).
    // ---------------------------------------------------------------

    [Test]
    public void BraceInit_ThreeComponents_PickedOpaque_StaysThree()
    {
        var result = ColorTextRewriter.Rewrite(
            ColorMatchKind.BraceInit, "{255, 0, 0}", 1, 2, 3, 255);
        Assert.That(result, Is.EqualTo("{1, 2, 3}"));
    }

    [Test]
    public void BraceInit_ThreeComponents_PickedAlpha_BecomesFour()
    {
        var result = ColorTextRewriter.Rewrite(
            ColorMatchKind.BraceInit, "{255, 0, 0}", 1, 2, 3, 128);
        Assert.That(result, Is.EqualTo("{1, 2, 3, 128}"));
    }

    [Test]
    public void BraceInit_FourComponents_StaysFour()
    {
        var result = ColorTextRewriter.Rewrite(
            ColorMatchKind.BraceInit, "{255, 0, 0, 255}", 9, 9, 9, 255);
        Assert.That(result, Is.EqualTo("{9, 9, 9, 255}"));
    }
}
