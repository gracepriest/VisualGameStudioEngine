using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 26: multi-edge <c>Anchor</c>, which the designer refused until the front end allowed it.
///
/// <para>⛔⛔ <c>docs/HANDOFF.md</c> carried this as AN OPEN DECISION: <c>Anchor="Left,Top,Right"</c>
/// could not be generated, measured three ways on 2026-09-13, and the choice offered was "teach the
/// parser a bitwise Or (a language change with full-suite blast radius)" or "ship single-edge only".
/// Re-measured 2026-09-18 — all three spellings still fail — but a fourth route was never tried:
/// <c>btn.Anchor = 7</c> COMPILES in BasicLang and csc rejects it with CS0266, while
/// <c>(AnchorStyles)7</c> is ACCEPTED. The cast was the entire gap, and it was refused by one arm of
/// <c>SemanticAnalyzer.RejectImpossibleConversion</c> rather than by the parser.</para>
///
/// <para>⚠ AnchorStyles is a FLAGS enum: None=0, Top=1, Bottom=2, Left=4, Right=8 — verified against
/// the official enum documentation rather than recalled. ⛔ <c>DockStyle</c> numbers differently
/// (Left=3, not 4) and is NOT flags, so the two must never share a conversion.</para>
/// </summary>
[TestFixture]
public class FormAnchorEmissionTests
{
    private static FormDocument FormWith(string? anchor, string? dock = null)
    {
        var form = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "AnchorForm", Width = 800, Height = 450, Text = "A"
        };

        form.Controls.Add(new FormControl
        {
            Kind = "Button",
            Id = "btn",
            TabIndex = 0,
            Geometry = new PixelGeometry
            {
                X = 10, Y = 10, Width = 80, Height = 24, Anchor = anchor, Dock = dock
            }
        });

        return form;
    }

    private static RegionWriteResult Write(FormDocument form) =>
        RegionWriter.Write(
            "AnchorForm.bas",
            FormScaffolder.Create("AnchorForm", FormTarget.WinForms).CodeText,
            form,
            "AnchorForm.blform");

    // ==================================================================
    // Emission
    // ==================================================================

    /// <summary>⚠ A single edge keeps the READABLE spelling — `CType(1, …)` would be a regression.</summary>
    [Test]
    public void ASingleEdgeStillEmitsTheNamedMember()
    {
        var result = Write(FormWith("Top"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.False);
            Assert.That(result.Text, Does.Contain("btn.Anchor = AnchorStyles.Top"));
            Assert.That(result.Text, Does.Not.Contain("CType"));
        });
    }

    [TestCase("Top,Left", 5)]
    [TestCase("Left,Top,Right", 13)]
    [TestCase("Top,Bottom,Left,Right", 15)]
    [TestCase("Bottom,Right", 10)]
    public void MultipleEdgesEmitTheCombinedFlagsValue(string anchor, int expected)
    {
        var result = Write(FormWith(anchor));

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.False,
                string.Join("; ", result.Diagnostics.Select(d => d.Format())));
            Assert.That(result.Text, Does.Contain($"btn.Anchor = CType({expected}, AnchorStyles)"));
        });
    }

    /// <summary>
    /// ⚠ The combined value is opaque on its own — `CType(13, AnchorStyles)` tells a reader nothing.
    /// The edge names ride along as a comment so the generated region stays legible against the
    /// document it came from.
    /// </summary>
    [Test]
    public void TheCombinedValueCarriesItsEdgeNamesAsAComment()
    {
        Assert.That(Write(FormWith("Left,Top,Right")).Text, Does.Contain("' Left, Top, Right"));
    }

    [Test]
    public void OrderDoesNotChangeTheValue()
    {
        Assert.That(
            Write(FormWith("Right,Left")).Text,
            Does.Contain("CType(12, AnchorStyles)"));
    }

    // ==================================================================
    // Refusals that must survive
    // ==================================================================

    /// <summary>
    /// ⛔ An edge name the enum does not have must still be refused. Summing it as zero would
    /// silently anchor the control to nothing, which is the D9 divergence this whole feature exists
    /// to prevent.
    /// </summary>
    [Test]
    public void AnUnknownEdgeNameIsStillRefused()
    {
        var result = Write(FormWith("Top,Sideways"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.True);
            Assert.That(
                string.Join(" ", result.Diagnostics.Select(d => d.Format())),
                Does.Contain("Sideways"));
        });
    }

    /// <summary>⛔ DockStyle is NOT flags and numbers differently — it must not take this path.</summary>
    [Test]
    public void DockStillEmitsItsNamedMember()
    {
        var result = Write(FormWith(anchor: null, dock: "Fill"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("btn.Dock = DockStyle.Fill"));
            Assert.That(result.Text, Does.Not.Contain("CType"));
        });
    }

    // ==================================================================
    // ⛔⛔ The only assertion that matters — csc
    // ==================================================================

    /// <summary>
    /// ⛔⛔ Every claim above is about TEXT. The catalog's own lesson is that text proves nothing
    /// here: WinForms members type as <c>Object</c> and a wrong spelling compiles green. This runs
    /// the designer's real output through the real compiler and then through csc.
    /// </summary>
    [TestCase("Top")]
    [TestCase("Top,Left")]
    [TestCase("Left,Top,Right")]
    [TestCase("Top,Bottom,Left,Right")]
    [Category("Integration")]
    public void TheEmittedAnchorCompiles(string anchor)
    {
        var written = Write(FormWith(anchor));
        Assert.That(written.Refused, Is.False);

        WinFormsCompile.AssertCompiles(
            WinFormsCatalogSweepTests.CompileToCSharp(written.Text),
            $"a control anchored to '{anchor}' must produce C# csc accepts");
    }
}
