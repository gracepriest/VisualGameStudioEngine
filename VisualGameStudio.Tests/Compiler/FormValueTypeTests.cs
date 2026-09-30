using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 3 Task 3 (plan 3.1, spec §2.2) — the new property TYPES: Font, Padding, Cursor. Each is three answers: the
/// stored form, the WinForms emission (ONE statement — the fan-in rule), and the web declaration(s); and each decides
/// Canon vs Degraded through <c>Accepts</c> (D9) — a malformed value is frozen and preserved, never coerced.
///
/// <para>⛔ The WinForms spellings are MEASURED (pre-flight §3): <c>9F</c> and <c>9.75F</c> lex; <c>Or</c> on flags does
/// not; <c>CType(n, FontStyle)</c> does.</para>
/// </summary>
[TestFixture]
public class FormValueTypeTests
{
    private static FormPropertyDef Def(FormPropertyType type, FormCssConverter converter = FormCssConverter.None,
        string? css = null) =>
        new("X", type, CssProperty: css, CssConverter: converter);

    private static readonly FormPropertyDef Font = Def(FormPropertyType.Font, FormCssConverter.Font, "font");
    private static readonly FormPropertyDef Padding = Def(FormPropertyType.Padding, FormCssConverter.Padding, "padding");
    private static readonly FormPropertyDef Cursor = Def(FormPropertyType.Cursor, FormCssConverter.Cursor, "cursor");

    // ==================================================================
    // Font — WinForms FontConverter text: "Segoe UI, 9pt, style=Bold, Italic"
    // ==================================================================

    [TestCase("Segoe UI, 9pt")]
    [TestCase("Segoe UI, 9.75pt, style=Bold, Italic")]
    [TestCase("Courier New, 12pt, style=Underline, Strikeout")]
    [TestCase("Arial, 10")]                          // no unit: points
    [TestCase("Arial,10pt,style=Bold")]              // no spaces
    [TestCase("Microsoft Sans Serif, 8.25pt, style=Regular")]
    public void AFont_InWinFormsOwnText_IsCanon(string value) =>
        Assert.That(Font.Accepts(value), Is.True);

    [TestCase("")]
    [TestCase("Segoe UI")]                           // no size
    [TestCase("Segoe UI, big")]
    [TestCase("Segoe UI, 0pt")]
    [TestCase("Segoe UI, -9pt")]
    [TestCase("Segoe UI, 9px")]                      // a unit this designer does not emit
    [TestCase("Segoe UI, 9pt, style=Heavy")]
    [TestCase("Segoe UI, 9pt, Bold")]                // styles need style=
    [TestCase("Seg\"oe, 9pt")]                       // a family that could break out of a string or a declaration
    [TestCase("Segoe;UI, 9pt")]
    [TestCase(", 9pt")]
    public void AMalformedFont_IsDegraded(string value) =>
        Assert.That(Font.Accepts(value), Is.False);

    [TestCase("segoe ui,9PT,style=italic, bold", "segoe ui, 9pt, style=Bold, Italic")]
    [TestCase("Arial, 10", "Arial, 10pt")]
    [TestCase("Arial, 10.50pt", "Arial, 10.5pt")]
    [TestCase("Arial, 10pt, style=Regular", "Arial, 10pt")]
    public void AFontsCanonicalText_IsFontConvertersShape_StylesInFlagOrder(string value, string canonical) =>
        Assert.That(Font.Canonical(value), Is.EqualTo(canonical));

    [TestCase("Segoe UI, 9pt", "New Font(\"Segoe UI\", 9F)")]
    [TestCase("Segoe UI, 9.75pt, style=Bold", "New Font(\"Segoe UI\", 9.75F, FontStyle.Bold)")]
    [TestCase("Segoe UI, 9pt, style=Bold, Italic", "New Font(\"Segoe UI\", 9F, CType(3, FontStyle))")]
    [TestCase("Arial, 12pt, style=Italic, Underline, Strikeout", "New Font(\"Arial\", 12F, CType(14, FontStyle))")]
    public void AFont_IsOneNewFontStatement_WithStylesAsACastWhenThereAreSeveral(string value, string literal) =>
        Assert.That(Font.WinFormsLiteral(value), Is.EqualTo(literal));

    [Test]
    public void AFont_OnThePage_IsItsFiveDeclarations_OnlyTheOnesItHas()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormCss.Declarations(Font, "Segoe UI, 9.75pt, style=Bold, Italic, Underline, Strikeout"),
                Is.EqualTo(new[]
                {
                    ("font-family", "\"Segoe UI\""), ("font-size", "9.75pt"), ("font-weight", "bold"),
                    ("font-style", "italic"), ("text-decoration", "underline line-through")
                }));
            Assert.That(FormCss.Declarations(Font, "Arial, 10pt"),
                Is.EqualTo(new[] { ("font-family", "\"Arial\""), ("font-size", "10pt") }),
                "a regular font says nothing about weight, style or decoration — the element keeps its own");
        });
    }

    [Test]
    public void FontsAreTheSameValue_IgnoringFamilyCase_AndSpelling()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Font.SameValue("Segoe UI, 9pt", "segoe ui,9"), Is.True);
            Assert.That(Font.SameValue("Segoe UI, 9pt, style=Bold", "Segoe UI, 9pt"), Is.False);
        });
    }

    // ==================================================================
    // Padding — "4" or "4, 2, 4, 2" (Left, Top, Right, Bottom — PaddingConverter's order)
    // ==================================================================

    [TestCase("4")]
    [TestCase("0")]
    [TestCase("4, 2, 4, 2")]
    [TestCase("4,2,4,2")]
    [TestCase(" 3 , 3 , 3 , 3 ")]
    public void APadding_IsCanon(string value) => Assert.That(Padding.Accepts(value), Is.True);

    [TestCase("")]
    [TestCase("-1")]
    [TestCase("4, 2")]
    [TestCase("4, 2, 4")]
    [TestCase("4px")]
    [TestCase("4, 2, 4, 2, 0")]
    public void AMalformedPadding_IsDegraded(string value) => Assert.That(Padding.Accepts(value), Is.False);

    [TestCase("3, 3, 3, 3", "3")]
    [TestCase("04", "4")]
    [TestCase("4,2,4,2", "4, 2, 4, 2")]
    public void APaddingsCanonicalText_IsUniformWhenItCanBe(string value, string canonical) =>
        Assert.That(Padding.Canonical(value), Is.EqualTo(canonical));

    [TestCase("4", "New Padding(4)")]
    [TestCase("3, 3, 3, 3", "New Padding(3)")]
    [TestCase("4, 2, 4, 1", "New Padding(4, 2, 4, 1)")]
    public void APadding_IsOneNewPaddingStatement(string value, string literal) =>
        Assert.That(Padding.WinFormsLiteral(value), Is.EqualTo(literal));

    [TestCase("4", "4px")]
    [TestCase("4, 2, 6, 1", "2px 6px 1px 4px")]   // CSS: top right bottom left
    public void APadding_OnThePage_IsCssOrder(string value, string css) =>
        Assert.That(FormCss.Declarations(Padding, value), Is.EqualTo(new[] { ("padding", css) }));

    // ==================================================================
    // Cursor — a Cursors member; the web through ONE mapping table
    // ==================================================================

    [Test]
    public void EveryCursorsMember_IsCanon_OnWinForms()
    {
        // The 28 static properties of System.Windows.Forms.Cursors (reflected, pre-flight §3; the csc sweep gates them).
        Assert.That(FormCursors.Names, Has.Count.EqualTo(28));
        Assert.That(FormCursors.Names.Where(n => !Cursor.Accepts(n, FormTarget.WinForms)), Is.Empty);
    }

    [TestCase("hand", "Hand")]
    [TestCase("IBEAM", "IBeam")]
    public void ACursorsCanonicalSpelling_IsTheMembers(string value, string canonical) =>
        Assert.That(Cursor.Canonical(value), Is.EqualTo(canonical));

    [Test]
    public void ACursor_IsItsCursorsMember_InWinForms() =>
        Assert.That(Cursor.WinFormsLiteral("hand"), Is.EqualTo("Cursors.Hand"));

    [TestCase("Hand", "pointer")]
    [TestCase("IBeam", "text")]
    [TestCase("WaitCursor", "wait")]
    [TestCase("SizeWE", "ew-resize")]
    [TestCase("No", "not-allowed")]
    public void ACursor_OnThePage_IsItsCssCursor(string value, string css) =>
        Assert.That(FormCss.Declarations(Cursor, value), Is.EqualTo(new[] { ("cursor", css) }));

    /// <summary>
    /// ⛔ The system-colour rule, for cursors: a member with no CSS equivalent is WinForms-only as a VALUE — refused on
    /// the web with a reason, never approximated with a different cursor.
    /// </summary>
    [Test]
    public void ACursorWithNoCssEquivalent_IsRefusedOnTheWeb_WithAReason()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Cursor.Accepts("UpArrow", FormTarget.WinForms), Is.True);
            Assert.That(Cursor.Accepts("UpArrow", FormTarget.Web), Is.False);
            Assert.That(Cursor.DescribeRefusal("UpArrow", FormTarget.Web), Does.Contain("UpArrow").And.Contain("CSS"));
            Assert.That(FormCss.Declarations(Cursor, "UpArrow"), Is.Empty);
        });
    }

    [TestCase("Pointer")]      // the CSS word, not a Cursors member
    [TestCase("")]
    public void AnUnknownCursor_IsDegraded(string value) => Assert.That(Cursor.Accepts(value), Is.False);

    // ==================================================================
    // The type-level invariants
    // ==================================================================

    /// <summary>A Degraded value never becomes source: WinFormsLiteral declines it (the region writer then skips it).</summary>
    [TestCase(FormPropertyType.Font, "Segoe UI")]
    [TestCase(FormPropertyType.Padding, "4, 2")]
    [TestCase(FormPropertyType.Cursor, "Pointer")]
    public void AMalformedValue_HasNoWinFormsLiteral(FormPropertyType type, string value) =>
        Assert.That(Def(type).WinFormsLiteral(value), Is.Null);

    /// <summary>Canonical of a malformed value is the value UNCHANGED — Degraded text is preserved, never coerced.</summary>
    [TestCase(FormPropertyType.Font, "Segoe UI")]
    [TestCase(FormPropertyType.Padding, "4, 2")]
    [TestCase(FormPropertyType.Cursor, "Pointer")]
    public void AMalformedValue_IsCanonicalisedToItself(FormPropertyType type, string value) =>
        Assert.That(Def(type).Canonical(value), Is.EqualTo(value));

    [Test]
    public void TheCursorRowOffersTheCursorsMembers_AsItsChoices() =>
        Assert.That(Cursor.Choices, Is.EqualTo(FormCursors.Names));

    /// <summary>
    /// ⛔ Scope call S1's rule, as a gate: a TYPE with no row cannot be put through the csc sweep (it is row-driven), so
    /// every type must be used by at least one catalog row — the Form's included. A type added "for later" is the
    /// no-caller failure CLAUDE.md records five times.
    /// </summary>
    [Test]
    public void EveryPropertyType_IsUsedByARow()
    {
        var used = FormControlCatalog.All.Append(FormControlCatalog.FormRoot)
            .SelectMany(d => d.Properties.Select(p => p.Type))
            .ToHashSet();

        Assert.That(Enum.GetValues<FormPropertyType>().Where(t => !used.Contains(t)), Is.Empty);
    }

    /// <summary>
    /// Font and Cursor where WinForms browses them (the snapshot decides, and the parity test names a miss); Padding on
    /// the content controls (pre-flight §3: a container's Padding moves DOCKED children, which FormDockLayout does not
    /// model, so no container carries it yet).
    /// </summary>
    [Test]
    public void TheNewTypes_ArriveWithTheirRows()
    {
        Assert.Multiple(() =>
        {
            foreach (var kind in new[] { "Label", "TextBox", "Button", "CheckBox", "RadioButton", "ComboBox", "ListBox",
                                          "Panel", "GroupBox", "LinkLabel", "NumericUpDown", "DateTimePicker" })
            {
                Assert.That(FormControlCatalog.Find(kind)!.Property("Font")?.Type, Is.EqualTo(FormPropertyType.Font), kind);
                Assert.That(FormControlCatalog.Find(kind)!.Property("Cursor")?.Type, Is.EqualTo(FormPropertyType.Cursor), kind);
            }

            foreach (var kind in new[] { "PictureBox", "TrackBar", "ProgressBar", "DataGridView" })
            {
                Assert.That(FormControlCatalog.Find(kind)!.Property("Font"), Is.Null, $"{kind}: WinForms hides its Font");
            }

            foreach (var kind in new[] { "Label", "Button", "CheckBox", "RadioButton", "LinkLabel" })
            {
                Assert.That(FormControlCatalog.Find(kind)!.Property("Padding")?.Type, Is.EqualTo(FormPropertyType.Padding), kind);
            }

            Assert.That(FormControlCatalog.Find("Panel")!.Property("Padding"), Is.Null, "no container Padding yet");
            Assert.That(FormControlCatalog.Find("TextBox")!.Property("Cursor")!.Default, Is.EqualTo("IBeam"),
                "TextBox's cursor is not ambient (the snapshot's 'reset')");
        });
    }
}
