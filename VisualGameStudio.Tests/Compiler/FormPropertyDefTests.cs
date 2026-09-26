using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>Spec §2.1/§2.2/§2.7/§2.8 — what one property row knows about its own values.</summary>
[TestFixture]
public class FormPropertyDefTests
{
    private static readonly string[] Nine =
    {
        "TopLeft", "TopCenter", "TopRight", "MiddleLeft", "MiddleCenter", "MiddleRight",
        "BottomLeft", "BottomCenter", "BottomRight"
    };

    private static FormPropertyDef AlignedDef() => new(
        "TextAlign", FormPropertyType.Enum, "TopLeft", Nine,
        WinFormsEnumType: "ContentAlignment",
        Aliases: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Left"] = "MiddleLeft", ["Center"] = "MiddleCenter", ["Right"] = "MiddleRight"
        });

    [Test]
    public void ALegacyAlias_IsCanon_ResolvesToItsCanonicalMember_AndEmitsIt()
    {
        var def = AlignedDef();

        Assert.Multiple(() =>
        {
            Assert.That(def.Accepts("Center"), Is.True, "an existing document must stay Canon (spec §2.8)");
            Assert.That(def.Canonical("Center"), Is.EqualTo("MiddleCenter"));
            Assert.That(def.Canonical("center"), Is.EqualTo("MiddleCenter"), "case-insensitively, as Accepts is");
            Assert.That(def.WinFormsLiteral("Center"), Is.EqualTo("ContentAlignment.MiddleCenter"));
            Assert.That(def.IsSourceForm("ContentAlignment.MiddleCenter"), Is.True);
        });
    }

    [Test]
    public void AnAlias_IsAcceptedButNeverOffered()
    {
        var def = AlignedDef();

        Assert.That(def.AllowedValues, Is.EqualTo(Nine), "AllowedValues holds the nine canonical members ONLY");
    }

    [Test]
    public void ACanonicalMember_KeepsTheCatalogsSpelling_WhateverCaseTheDocumentUsed()
    {
        Assert.That(AlignedDef().Canonical("middleright"), Is.EqualTo("MiddleRight"));
    }

    [Test]
    public void AValueTheRowDoesNotKnow_IsNotCanonicalised()
    {
        Assert.That(AlignedDef().Canonical("Bogus"), Is.EqualTo("Bogus"),
            "a Degraded value is preserved exactly — never coerced");
    }

    [Test]
    public void DefaultFor_UsesWebDefault_OnlyOnTheWeb()
    {
        var def = new FormPropertyDef("Interval", FormPropertyType.Int, "100", WebDefault: "250");

        Assert.Multiple(() =>
        {
            Assert.That(def.DefaultFor(FormTarget.WinForms), Is.EqualTo("100"));
            Assert.That(def.DefaultFor(FormTarget.Web), Is.EqualTo("250"));
        });
    }

    [Test]
    public void AnEmptyWebDefault_MeansNoDefaultOnTheWeb()
    {
        var def = new FormPropertyDef("BackColor", FormPropertyType.Color, "Control", WebDefault: "");

        Assert.That(def.DefaultFor(FormTarget.Web), Is.Null);
    }

    [Test]
    public void WithNoWebDefault_TheWebUsesTheWinFormsDefault()
    {
        Assert.That(new FormPropertyDef("Enabled", FormPropertyType.Bool, "true").DefaultFor(FormTarget.Web),
            Is.EqualTo("true"));
    }

    [TestCase("75, 23", true)]
    [TestCase("75,23", true)]
    [TestCase(" 800 , 450 ", true)]
    [TestCase("75x23", false)]
    [TestCase("75", false)]
    [TestCase("a, b", false)]
    [TestCase("1, 2, 3", false)]
    public void Size_AcceptsWidthCommaHeight(string value, bool expected)
    {
        Assert.That(new FormPropertyDef("ClientSize", FormPropertyType.Size).Accepts(value), Is.EqualTo(expected));
    }

    [Test]
    public void Size_EmitsOneNewSizeStatement_NeverMemberWise()
    {
        var def = new FormPropertyDef("ClientSize", FormPropertyType.Size);

        Assert.Multiple(() =>
        {
            Assert.That(def.WinFormsLiteral("800, 450"), Is.EqualTo("New Size(800, 450)"),
                "the fan-in rule: ONE statement — ClientSize.Width = … is CS1612");
            Assert.That(def.IsSourceForm("New Size(800, 450)"), Is.True);
        });
    }

    [Test]
    public void ACanonicalBool_IsLowerCase_TheDocumentsOwnSpelling()
    {
        var def = new FormPropertyDef("Enabled", FormPropertyType.Bool, "true");

        Assert.That(def.Canonical("True"), Is.EqualTo("true"));
    }
}
