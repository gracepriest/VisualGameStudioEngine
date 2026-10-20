using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 30's marker contract. The style is read from the hash-guarded markers, never guessed from
/// the target or from what the regions happen to contain.
/// </summary>
[TestFixture]
public class FormCodeStyleTests
{
    private static string Regions(string formFileName, string? style, string content = "")
    {
        var hash = RegionMarkers.HashContent(content);
        return $"""
            Public Class F
            {RegionMarkers.FormatOpen("controls", formFileName, hash, "    ", style)}
            {content}{RegionMarkers.FormatClose("    ")}

            {RegionMarkers.FormatOpen("init", formFileName, hash, "    ", style)}
            {content}{RegionMarkers.FormatClose("    ")}
            End Class
            """;
    }

    [Test]
    public void FormatOpen_WithoutAStyle_IsTheLegacyMarkerByteForByte()
    {
        Assert.That(
            RegionMarkers.FormatOpen("controls", "F.blwebform", "abcd", "    "),
            Is.EqualTo("    ' <vgs:designer region=\"controls\" form=\"F.blwebform\" hash=\"abcd\">"));
    }

    [Test]
    public void FormatOpen_WithAStyle_PutsItBetweenFormAndHash()
    {
        Assert.That(
            RegionMarkers.FormatOpen("controls", "F.blwebform", "abcd", "    ", "portable"),
            Is.EqualTo(
                "    ' <vgs:designer region=\"controls\" form=\"F.blwebform\" style=\"portable\" hash=\"abcd\">"));
    }

    [Test]
    public void Scan_RecordsTheRawStyle()
    {
        var regions = RegionMarkers.Scan(Regions("F.blwebform", "portable"));

        Assert.That(regions, Has.All.Property(nameof(FormRegion.Style)).EqualTo("portable"));
    }

    [Test]
    public void NoStyle_OnAWebForm_IsTheHistoricalDomStyle()
    {
        var read = FormCodeStyle.Of(Regions("F.blwebform", null), "F.blwebform");

        Assert.Multiple(() =>
        {
            Assert.That(read.Style, Is.EqualTo(FormStyle.Dom));
            Assert.That(read.Problem, Is.Null);
        });
    }

    [Test]
    public void NoStyle_OnAWinFormsDocument_IsTheHistoricalWinFormsStyle()
    {
        var read = FormCodeStyle.Of(Regions("F.blform", null), "F.blform");

        Assert.Multiple(() =>
        {
            Assert.That(read.Style, Is.EqualTo(FormStyle.WinForms));
            Assert.That(read.Problem, Is.Null);
        });
    }

    [Test]
    public void Portable_OnAWebDocument_IsPortable()
    {
        var read = FormCodeStyle.Of(Regions("F.blwebform", "portable"), "F.blwebform");

        Assert.Multiple(() =>
        {
            Assert.That(read.Style, Is.EqualTo(FormStyle.Portable));
            Assert.That(read.Problem, Is.Null);
        });
    }

    [Test]
    public void Dual_OnAWinFormsDocument_IsAccepted_ForThePieceThreeContract()
    {
        var read = FormCodeStyle.Of(Regions("F.blform", "dual"), "F.blform");

        Assert.Multiple(() =>
        {
            Assert.That(read.Style, Is.EqualTo(FormStyle.Dual));
            Assert.That(read.Problem, Is.Null);
        });
    }

    [Test]
    public void Portable_OnAWinFormsDocument_IsMalformed()
    {
        var read = FormCodeStyle.Of(Regions("F.blform", "portable"), "F.blform");

        Assert.That(read.Problem, Does.Contain("only legal on a .blwebform"));
    }

    [Test]
    public void Dual_OnAWebDocument_IsMalformed()
    {
        var read = FormCodeStyle.Of(Regions("F.blwebform", "dual"), "F.blwebform");

        Assert.That(read.Problem, Does.Contain("only legal on a .blform"));
    }

    [Test]
    public void UnknownStyle_OnAWebDocument_IsMalformed()
    {
        var read = FormCodeStyle.Of(Regions("F.blwebform", "winforms"), "F.blwebform");

        Assert.That(read.Problem, Does.Contain("unknown code style"));
    }

    [Test]
    public void UnknownStyle_OnAWinFormsDocument_IsMalformed()
    {
        var read = FormCodeStyle.Of(Regions("F.blform", "portable-ish"), "F.blform");

        Assert.That(read.Problem, Does.Contain("unknown code style"));
    }

    [Test]
    public void ExplicitEmptyStyle_IsUnknown_NotTheHistoricalDefault()
    {
        var empty = RegionMarkers.HashContent("");
        var source = $"""
            Public Class F
            {RegionMarkers.FormatOpen("controls", "F.blwebform", empty, "    ", "")}
            {RegionMarkers.FormatClose("    ")}
            {RegionMarkers.FormatOpen("init", "F.blwebform", empty, "    ", "")}
            {RegionMarkers.FormatClose("    ")}
            End Class
            """;

        // FormatOpen omits an empty style, so inject the attribute directly to prove the reader's
        // distinction: absent is default, present-but-empty is a value it does not know.
        source = source.Replace("form=\"F.blwebform\" hash=", "form=\"F.blwebform\" style=\"\" hash=");
        var read = FormCodeStyle.Of(source, "F.blwebform");

        Assert.That(read.Problem, Does.Contain("unknown code style"));
    }

    [Test]
    public void DisagreeingRegions_AreMalformed()
    {
        var empty = RegionMarkers.HashContent("");
        var source = $"""
            Public Class F
            {RegionMarkers.FormatOpen("controls", "F.blwebform", empty, "    ", "portable")}
            {RegionMarkers.FormatClose("    ")}
            {RegionMarkers.FormatOpen("init", "F.blwebform", empty, "    ")}
            {RegionMarkers.FormatClose("    ")}
            End Class
            """;

        var read = FormCodeStyle.Of(source, "F.blwebform");

        Assert.That(read.Problem, Does.Contain("disagree"));
    }

    [Test]
    public void Writer_RefusesAnUnknownStyle_WithBL8012AndNoWrite()
    {
        var source = Regions("F.blwebform", "bogus");
        var result = RegionWriter.Write(
            "F.bas", source, new FormDocument { Target = FormTarget.Web, Name = "F" }, "F.blwebform");

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.True);
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Text, Is.EqualTo(source), "a style refusal changes no byte");
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.RegionMalformed));
        });
    }

    [Test]
    public void Writer_PreservesTheRecordedPortableStyle_InBothRewrittenMarkers()
    {
        var scaffold = FormScaffolder.Create("LoginForm", FormTarget.Web, portable: true);
        var form = new FormDocument { Target = FormTarget.Web, Name = "LoginForm" };
        form.Controls.Add(new FormControl { Kind = "Button", Id = "btnLogin" });

        var result = RegionWriter.Write(
            scaffold.CodeFileName, scaffold.CodeText, form, scaffold.DocumentFileName);

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.False,
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            Assert.That(RegionMarkers.Scan(result.Text), Has.All.Property(nameof(FormRegion.Style))
                .EqualTo("portable"));
            Assert.That(result.Text, Does.Contain("btnLogin = New Button()"));
        });
    }
}
