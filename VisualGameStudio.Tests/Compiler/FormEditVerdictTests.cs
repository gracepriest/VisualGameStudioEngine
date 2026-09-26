using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <see cref="FormPropertyDef.Judge"/> — the property grid's four-way decision about a pushed value
/// (no-op / reset / refuse / write), as a pure function of the catalog row, so every rule is a table row
/// here rather than a view-model fixture. The row (FormPropertyRow.Commit) only carries out the verdict.
/// </summary>
[TestFixture]
public class FormEditVerdictTests
{
    private static FormPropertyDef Def(string kind, string property) =>
        FormControlCatalog.Find(kind)?.Property(property)
        ?? throw new InvalidOperationException($"the catalog has no {kind}.{property}");

    // present = the document's text, or null when the document does not carry the property.
    [TestCase("TextBox", "MaxLength", "0", null, FormTarget.Web, FormEditVerdict.NoOp,
        TestName = "NullDefault_AbsentInt_TheEditorsZeroEcho_IsANoOp")]
    [TestCase("TextBox", "MaxLength", "10", null, FormTarget.Web, FormEditVerdict.Write,
        TestName = "NullDefault_AbsentInt_ARealNumber_IsWritten")]
    [TestCase("TextBox", "MaxLength", "0", "5", FormTarget.Web, FormEditVerdict.Write,
        TestName = "NullDefault_PresentInt_Zero_IsARealEdit")]
    [TestCase("TextBox", "MaxLength", "32767", null, FormTarget.WinForms, FormEditVerdict.NoOp,
        TestName = "AbsentInt_ItsDisplayedDefault_IsANoOp")]
    [TestCase("TextBox", "MaxLength", "0", null, FormTarget.WinForms, FormEditVerdict.Write,
        TestName = "AbsentInt_WithADefault_Zero_IsARealEdit")]
    [TestCase("Label", "Enabled", "true", null, FormTarget.WinForms, FormEditVerdict.NoOp,
        TestName = "AbsentBool_ItsDisplayedDefault_IsANoOp")]
    [TestCase("Label", "Enabled", "false", null, FormTarget.WinForms, FormEditVerdict.Write,
        TestName = "AbsentBool_TheOtherValue_IsWritten")]
    [TestCase("Button", "TextAlign", "MiddleLeft", "Left", FormTarget.WinForms, FormEditVerdict.NoOp,
        TestName = "Alias_PushedBackAsItsMember_IsANoOp")]
    [TestCase("Button", "TextAlign", "middleleft", "MiddleLeft", FormTarget.WinForms, FormEditVerdict.NoOp,
        TestName = "Enum_CaseOnly_IsANoOp")]
    [TestCase("NumericUpDown", "Value", "7", "007", FormTarget.Web, FormEditVerdict.NoOp,
        TestName = "Int_SameNumber_DifferentSpelling_IsANoOp")]
    [TestCase("Label", "BackColor", "", "Red", FormTarget.WinForms, FormEditVerdict.Reset,
        TestName = "ClearingAPresentTypedValue_IsReset")]
    [TestCase("Label", "BackColor", "", null, FormTarget.WinForms, FormEditVerdict.NoOp,
        TestName = "ClearingAnAbsentNullDefaultRow_IsANoOp")]
    [TestCase("Label", "Text", "", "Hi", FormTarget.WinForms, FormEditVerdict.Write,
        TestName = "ClearingAString_WritesTheEmptyString")]
    [TestCase("Label", "Enabled", "maybe", null, FormTarget.WinForms, FormEditVerdict.Refuse,
        TestName = "InvalidBool_IsRefused")]
    [TestCase("Label", "TextAlign", "Bogus", null, FormTarget.WinForms, FormEditVerdict.Refuse,
        TestName = "InvalidEnum_IsRefused")]
    [TestCase("Label", "ForeColor", "12345", null, FormTarget.WinForms, FormEditVerdict.Refuse,
        TestName = "InvalidColour_IsRefused")]
    [TestCase("Label", "ForeColor", "ActiveCaption", null, FormTarget.Web, FormEditVerdict.Refuse,
        TestName = "ASystemColourOnTheWeb_IsRefused")]
    [TestCase("Label", "ForeColor", "ActiveCaption", null, FormTarget.WinForms, FormEditVerdict.Write,
        TestName = "ASystemColourOnWinForms_IsWritten")]
    [TestCase("Label", "BackColor", "#FF0000", "#ff0000", FormTarget.WinForms, FormEditVerdict.NoOp,
        TestName = "HexColour_CaseOnly_IsANoOp")]
    public void Judge(string kind, string property, string value, string? present, FormTarget target,
        FormEditVerdict expected)
    {
        Assert.That(Def(kind, property).Judge(value, present, target), Is.EqualTo(expected));
    }

    [Test]
    public void NullDefault_AbsentBool_TheEditorsFalseEcho_IsANoOp()
    {
        // No catalog Bool has a null default today, so the row is built here: the rule is by TYPE.
        var def = new FormPropertyDef("Flag", FormPropertyType.Bool);

        Assert.Multiple(() =>
        {
            Assert.That(def.Judge("false", null, FormTarget.WinForms), Is.EqualTo(FormEditVerdict.NoOp));
            Assert.That(def.Judge("true", null, FormTarget.WinForms), Is.EqualTo(FormEditVerdict.Write));
        });
    }

    [Test]
    public void Displayed_IsTheCanonicalValueWhenPresent_AndTheTargetsDefaultWhenAbsent()
    {
        var maxLength = Def("TextBox", "MaxLength");
        var align = Def("Button", "TextAlign");

        Assert.Multiple(() =>
        {
            Assert.That(maxLength.Displayed(null, FormTarget.WinForms), Is.EqualTo("32767"));
            Assert.That(maxLength.Displayed(null, FormTarget.Web), Is.EqualTo(""), "no web default");
            Assert.That(maxLength.Displayed("007", FormTarget.Web), Is.EqualTo("7"));
            Assert.That(align.Displayed("Left", FormTarget.WinForms), Is.EqualTo("MiddleLeft"));
        });
    }
}
