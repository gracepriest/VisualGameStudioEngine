using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec 2026-09-27 §2.1 — a document's layout VOCABULARY (pixels, or cells) is decided by
/// (target, layout) through ONE helper. Every site that used to decide by target alone asks it.
/// </summary>
[TestFixture]
public class FormVocabularyTests
{
    [TestCase(FormTarget.WinForms, null, true)]
    [TestCase(FormTarget.WinForms, FormLayoutKind.Grid, true)]
    [TestCase(FormTarget.Web, null, false)]
    [TestCase(FormTarget.Web, FormLayoutKind.Grid, false)]
    [TestCase(FormTarget.Web, FormLayoutKind.Flow, false)]
    [TestCase(FormTarget.Web, FormLayoutKind.Canvas, true)]
    public void IsPixel_IsWinForms_OrAWebCanvas(FormTarget target, FormLayoutKind? layout, bool expected) =>
        Assert.That(FormVocabulary.IsPixel(target, layout), Is.EqualTo(expected));

    [Test]
    public void LayoutOf_AWebDocumentWithNoLayout_IsGrid_AsFormLayoutDefaults()
    {
        var page = new FormDocument { Target = FormTarget.Web, Name = "P" };

        Assert.Multiple(() =>
        {
            Assert.That(FormVocabulary.LayoutOf(page), Is.EqualTo(FormLayoutKind.Grid));
            Assert.That(new FormLayout().Kind, Is.EqualTo(FormLayoutKind.Grid),
                "the two defaults are one fact — a page with no <Layout> is a Grid page");
            Assert.That(FormVocabulary.IsPixel(page), Is.False);
        });
    }

    [Test]
    public void LayoutOf_ACanvasPage_IsCanvas_AndThePageSpeaksPixels()
    {
        var page = new FormDocument
        {
            Target = FormTarget.Web, Name = "P", Layout = new FormLayout { Kind = FormLayoutKind.Canvas }
        };

        Assert.Multiple(() =>
        {
            Assert.That(FormVocabulary.LayoutOf(page), Is.EqualTo(FormLayoutKind.Canvas));
            Assert.That(FormVocabulary.IsPixel(page), Is.True);
        });
    }

    [Test]
    public void LayoutOf_AWinFormsDocument_IsNull_EvenWithAStrayLayout()
    {
        var window = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "W", Layout = new FormLayout { Kind = FormLayoutKind.Grid }
        };

        Assert.Multiple(() =>
        {
            Assert.That(FormVocabulary.LayoutOf(window), Is.Null, "a window has no <Layout> (D3)");
            Assert.That(FormVocabulary.IsPixel(window), Is.True);
        });
    }

    [Test]
    public void IsPixel_AndLayoutOf_RefuseANullDocument_WithArgumentNullException()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => FormVocabulary.IsPixel((FormDocument)null!));
            Assert.Throws<ArgumentNullException>(() => FormVocabulary.LayoutOf(null!));
        });
    }
}
