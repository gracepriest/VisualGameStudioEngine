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
        // The REAL catalog row, not a fixture built here — asserting a def this test constructed would
        // only prove the constructor stores what it is given.
        var textAlign = FormControlCatalog.Find("Label")!.Property("TextAlign")!;

        Assert.Multiple(() =>
        {
            Assert.That(textAlign.AllowedValues, Is.Not.Null);
            Assert.That(textAlign.AllowedValues!, Has.None.EqualTo("Left").IgnoreCase);
            Assert.That(textAlign.AllowedValues!, Has.None.EqualTo("Center").IgnoreCase);
            Assert.That(textAlign.AllowedValues!, Has.None.EqualTo("Right").IgnoreCase);
            Assert.That(textAlign.Accepts("Left"), Is.True, "an existing document's Left stays Canon");
            Assert.That(textAlign.Canonical("Left"), Is.EqualTo("MiddleLeft"));
            Assert.That(textAlign.WinFormsLiteral("Left"), Is.EqualTo("ContentAlignment.MiddleLeft"));
        });
    }

    [Test]
    public void AnAliasKeyThatIsAlsoAMemberName_TheMemberWins()
    {
        var def = new FormPropertyDef("TextAlign", FormPropertyType.Enum, "MiddleLeft", Nine,
            WinFormsEnumType: "ContentAlignment",
            Aliases: new Dictionary<string, string> { ["MiddleLeft"] = "MiddleRight" });

        Assert.That(def.Canonical("MiddleLeft"), Is.EqualTo("MiddleLeft"),
            "a member is never re-mapped — AllowedValues is consulted before Aliases");
    }

    [Test]
    public void Aliases_AreCaseInsensitive_WhateverComparerTheCallerBuiltThemWith()
    {
        // Built with the DEFAULT (ordinal) comparer on purpose: the row normalises it.
        var def = new FormPropertyDef("TextAlign", FormPropertyType.Enum, "MiddleLeft", Nine,
            WinFormsEnumType: "ContentAlignment",
            Aliases: new Dictionary<string, string> { ["Left"] = "MiddleLeft" });

        Assert.Multiple(() =>
        {
            Assert.That(def.Accepts("LEFT"), Is.True);
            Assert.That(def.Canonical("left"), Is.EqualTo("MiddleLeft"));
            Assert.That((def with { Aliases = new Dictionary<string, string> { ["Right"] = "MiddleRight" } })
                .Canonical("RIGHT"), Is.EqualTo("MiddleRight"), "a `with` copy is normalised too");
        });
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

        Assert.Multiple(() =>
        {
            Assert.That(def.Canonical("True"), Is.EqualTo("true"));
            Assert.That(def.Canonical("FALSE"), Is.EqualTo("false"));
            Assert.That(def.Canonical("maybe"), Is.EqualTo("maybe"), "a non-bool is Degraded and preserved, never coerced");
        });
    }

    // ------------------------------------------------------------------
    // Size — edges of the value parser.
    // ------------------------------------------------------------------

    [Test]
    public void Size_ANegativeDimension_IsAccepted_AsWinFormsSizeIs()
    {
        var def = new FormPropertyDef("ClientSize", FormPropertyType.Size);

        Assert.Multiple(() =>
        {
            Assert.That(def.Accepts("-5, 10"), Is.True);
            Assert.That(def.WinFormsLiteral("-5, 10"), Is.EqualTo("New Size(-5, 10)"));
        });
    }

    [Test]
    public void Size_AnOverflowingDimension_IsRefused()
    {
        Assert.That(new FormPropertyDef("ClientSize", FormPropertyType.Size).Accepts("99999999999, 1"), Is.False);
    }

    [Test]
    [SetCulture("sv-SE")]
    public void Size_IsCultureInvariant_BothWays()
    {
        // sv-SE's NegativeSign is U+2212. Parsing it would make a document mean different things on
        // different machines, and FORMATTING with it emits `New Size(−5, 10)` — CS1056 at csc.
        var def = new FormPropertyDef("ClientSize", FormPropertyType.Size);

        Assert.Multiple(() =>
        {
            Assert.That(def.Accepts("−5, 10"), Is.False, "U+2212 is not a minus in the document");
            Assert.That(def.WinFormsLiteral("-5, 10"), Is.EqualTo("New Size(-5, 10)"),
                "ASCII minus in the generated source whatever the current culture");
        });
    }

    [Test]
    public void Size_SpacedInput_IsNormalisedInTheLiteral()
    {
        Assert.That(new FormPropertyDef("ClientSize", FormPropertyType.Size).WinFormsLiteral(" 800 , 450 "),
            Is.EqualTo("New Size(800, 450)"));
    }

    [TestCase("New Size(800, 450)", true)]
    [TestCase("New Size(800,450)", true)]
    [TestCase("New Size(1, 2) + junk", false)]
    [TestCase("New Size(1, 2) + New Size(3)", false)]
    [TestCase("New Size(a, b)", false)]
    [TestCase("New Size(1)", false)]
    public void Size_IsSourceForm_OnlyForAParsableNewSize(string value, bool expected)
    {
        Assert.That(new FormPropertyDef("ClientSize", FormPropertyType.Size).IsSourceForm(value), Is.EqualTo(expected));
    }

    // ------------------------------------------------------------------
    // System colours (pulled forward from Task 4: the CS0117 fix landed in Task 2).
    // ------------------------------------------------------------------

    [Test]
    public void ASystemColour_IsSystemColorsOnWinForms_NeverColorDot()
    {
        var fore = new FormPropertyDef("ForeColor", FormPropertyType.Color);

        Assert.Multiple(() =>
        {
            Assert.That(fore.WinFormsLiteral("Control"), Is.EqualTo("SystemColors.Control"),
                "Color.Control does not exist — CS0117 at csc, BasicLang silent");
            Assert.That(fore.WinFormsLiteral("windowtext"), Is.EqualTo("SystemColors.WindowText"),
                "the canonical spelling, whatever case the document used");
            Assert.That(fore.IsSourceForm("SystemColors.Control"), Is.True);
            Assert.That(fore.WinFormsLiteral("Red"), Is.EqualTo("Color.Red"), "a KnownColor is still a Color member");
        });
    }

    [Test]
    public void ASystemColourWithNoCssEquivalent_IsRefusedOnTheWebOnly()
    {
        var fore = new FormPropertyDef("ForeColor", FormPropertyType.Color);

        Assert.Multiple(() =>
        {
            Assert.That(fore.Accepts("ActiveCaption", FormTarget.WinForms), Is.True);
            Assert.That(fore.Accepts("ActiveCaption", FormTarget.Web), Is.False);
            Assert.That(fore.Accepts("Control", FormTarget.Web), Is.True, "Control has a CSS equivalent: ButtonFace");
            Assert.That(fore.DescribeRefusal("ActiveCaption", FormTarget.Web), Does.Contain("no CSS equivalent"));
        });
    }

    [Test]
    public void HotTrack_IsRefusedOnTheWeb_BecauseLinkTextIsOnlyAnApproximation()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormSystemColors.CssFor("HotTrack"), Is.Null);
            Assert.That(new FormPropertyDef("ForeColor", FormPropertyType.Color).Accepts("HotTrack", FormTarget.Web),
                Is.False);
        });
    }

    [TestCase("SystemColors.Bogus")]
    [TestCase("SystemColors.control")]
    [TestCase("SystemColors.")]
    [TestCase("SystemColors.Control + junk")]
    public void AnUnknownSystemColorsMember_IsNotSourceForm(string value)
    {
        Assert.That(new FormPropertyDef("ForeColor", FormPropertyType.Color).IsSourceForm(value), Is.False,
            "only a member the table names is source — SystemColors.Bogus is CS0117 at csc");
    }

    [Test]
    public void ASystemColour_IsCanonicalisedToTheTablesSpelling()
    {
        var fore = new FormPropertyDef("ForeColor", FormPropertyType.Color);

        Assert.Multiple(() =>
        {
            Assert.That(fore.Canonical("control"), Is.EqualTo("Control"));
            Assert.That(fore.Canonical("#FF0000"), Is.EqualTo("#FF0000"), "a non-system colour is unchanged");
        });
    }

    // ------------------------------------------------------------------
    // DescribeRefusal — the SAME predicate as Accepts(value, target).
    // ------------------------------------------------------------------

    [Test]
    public void DescribeRefusal_OfAnAcceptedValue_Throws_ThereIsNoRefusalToDescribe()
    {
        var fore = new FormPropertyDef("ForeColor", FormPropertyType.Color);

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => fore.DescribeRefusal("#FF0000", FormTarget.Web));
            Assert.Throws<ArgumentException>(() => fore.DescribeRefusal("Control", FormTarget.Web),
                "Control maps to ButtonFace — calling it 'no CSS equivalent' would be false");
            Assert.Throws<ArgumentException>(() => fore.DescribeRefusal("ActiveCaption", FormTarget.WinForms));
        });
    }

    [Test]
    public void ASystemColourName_OnANonColourRow_IsNeverDescribedAsASystemColour()
    {
        var text = new FormPropertyDef("Text", FormPropertyType.String);
        var mode = new FormPropertyDef("Mode", FormPropertyType.Enum, "A", new[] { "A", "B" });

        Assert.Multiple(() =>
        {
            Assert.That(text.Accepts("Window", FormTarget.Web), Is.True, "a caption that says Window is just text");
            Assert.That(mode.DescribeRefusal("Menu", FormTarget.Web),
                Is.EqualTo("'Menu' is not a valid Enum (expected one of: A, B). The value is preserved exactly as written."));
        });
    }
}
