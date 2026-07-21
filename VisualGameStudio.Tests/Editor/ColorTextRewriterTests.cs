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
/// 0x{R}{G}{B}), uppercase X2 hex.
/// BraceInit's branch lands in a later task — until then it throws.
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
    // Future kinds — rewriter-first ordering: branches land in later
    // tasks, so until then the kind name must surface in the throw.
    // ---------------------------------------------------------------

    [Test]
    public void BraceInit_Throws_NotSupported_Yet()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ColorTextRewriter.Rewrite(ColorMatchKind.BraceInit, "{ 10, 20, 30, 255 }", 1, 2, 3, 255));
        Assert.That(ex!.Message, Does.Contain("BraceInit"));
    }
}
