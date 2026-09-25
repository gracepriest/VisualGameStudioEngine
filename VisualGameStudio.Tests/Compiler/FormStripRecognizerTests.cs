using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 18 (commit 24c) — the WinForms recognizer's <c>Controls.Add</c> arm
/// (<c>WinFormsDialect.cs:161</c>) has exactly one shape today:
/// <c>&lt;receiver&gt;.Controls.Add(&lt;id&gt;)</c>. A strip's own host verbs — spec §1's
/// <c>menuStrip1.Items.Add(mnuFile)</c> and <c>mnuFile.DropDownItems.Add(mnuOpen)</c> — do not match
/// it, so importing a hand-written menu via <c>design --check</c> reports both children BL8006
/// "created but never added to the form": true of neither. <c>DesignCheck.CheckSource</c> is the
/// entry point a user's own file actually goes through, so the fixture is driven there rather than
/// at <c>WinFormsDialect.Read</c> directly.
/// </summary>
[TestFixture]
public class FormStripRecognizerTests
{
    private const string MenuFormSource = """
        Public Class MenuForm
            Inherits Form

            Private menuStrip1 As MenuStrip
            Private mnuFile As ToolStripMenuItem
            Private mnuOpen As ToolStripMenuItem
            Private cmb As ComboBox

            Public Sub New()
                menuStrip1 = New MenuStrip()
                mnuFile = New ToolStripMenuItem()
                mnuOpen = New ToolStripMenuItem()
                mnuFile.DropDownItems.Add(mnuOpen)
                menuStrip1.Items.Add(mnuFile)
                Me.Controls.Add(menuStrip1)
                cmb = New ComboBox()
                cmb.Items.Add("Apple")
                Me.Controls.Add(cmb)
            End Sub
        End Class
        """;

    [Test]
    public void MenuStripItemsAdd_AndDropDownItemsAdd_ParentTheirChildren_NoOrphanReported()
    {
        var findings = DesignCheck.CheckSource("MenuForm.bas", MenuFormSource);

        var orphans = findings
            .Where(d => d.Code == DesignCodes.OrphanedControl)
            .Select(d => d.Message)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(orphans.Any(m => m.Contains("'mnuFile'")), Is.False,
                "menuStrip1.Items.Add(mnuFile) must parent mnuFile — today nothing does, so this fails");
            Assert.That(orphans.Any(m => m.Contains("'mnuOpen'")), Is.False,
                "mnuFile.DropDownItems.Add(mnuOpen) must parent mnuOpen — today nothing does, so this fails");
        });
    }

    /// <summary>
    /// ⚠ NOT itself red against today's code — a forward-compatibility pin. The arm added for
    /// <c>Items.Add</c>/<c>DropDownItems.Add</c> also sees a ComboBox's own
    /// <c>cmb.Items.Add("Apple")</c>: the same token shape, with a STRING LITERAL sitting where a
    /// child id would be. <c>MarkParented</c> requires an <c>Identifier</c> token at that position and
    /// a string literal is not one, so this must neither throw nor mark anything as parented by
    /// "Apple" — the same net effect as today's fallthrough, where the arm does not exist at all yet.
    /// Pinned so nobody later "fixes" the new arm to exclude ComboBox by name instead of relying on
    /// the token-type guard that already handles it.
    /// </summary>
    [Test]
    public void AComboBoxsItemsAdd_OfAStringLiteral_IsNotMistakenForParenting()
    {
        IReadOnlyList<DesignDiagnostic> findings = Array.Empty<DesignDiagnostic>();

        Assert.DoesNotThrow(() => findings = DesignCheck.CheckSource("MenuForm.bas", MenuFormSource));

        Assert.Multiple(() =>
        {
            Assert.That(findings.Any(d => d.Message.Contains("Apple")), Is.False,
                "a string literal must never be read as a control id");
            Assert.That(
                findings.Any(d => d.Code == DesignCodes.OrphanedControl && d.Message.Contains("'cmb'")),
                Is.False,
                "cmb IS parented, via its own separate Me.Controls.Add(cmb)");
        });
    }
}
