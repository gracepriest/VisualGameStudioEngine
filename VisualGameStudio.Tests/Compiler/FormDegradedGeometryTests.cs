using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 3 Task 1 — the backlog carried from slice 1's culture review (plan 2026-09-25, slice 3 top note):
/// an unreadable control coordinate or tab index used to read as 0 with NO diagnostic, so the program put
/// the control at the origin silently. Now it is D9's Degraded tier — frozen in the grid, preserved in the
/// document, reported by <c>design --check</c> — exactly as a Degraded ClientSize is. ⛔ A U+2212 minus is
/// still NOT a number (a document means one number on every machine); the reason says so by name.
/// </summary>
[TestFixture]
public class FormDegradedGeometryTests
{
    private static readonly string UnicodeMinus = ((char)0x2212).ToString();

    private static string WinForm(string buttonAttributes) => $"""
        <Form Name="F" Version="1" Width="400" Height="300">
          <Controls>
            <Button Id="btn" {buttonAttributes}/>
          </Controls>
        </Form>
        """;

    private static FormFile ReadWin(string buttonAttributes) =>
        FormDocumentReader.Read("F.blform", WinForm(buttonAttributes));

    /// <summary>
    /// ⚠ A tab is written as the <c>&amp;#9;</c> character reference: XML attribute-value normalisation turns a
    /// LITERAL tab into a space, which TryParseInt accepts — the document would then be readable and prove nothing.
    /// </summary>
    private const string TabbedX = "X=\"5&#9;\"";

    [Test]
    public void AnUnreadableCoordinate_IsDegraded_NotSilentlyZero()
    {
        var file = ReadWin(TabbedX + " Y=\"10\" Width=\"75\" Height=\"23\" TabIndex=\"0\"");

        Assert.Multiple(() =>
        {
            Assert.That(((PixelGeometry)file.Model.FindById("btn")!.Geometry!).X, Is.EqualTo(0),
                "the model still has no number for it");
            Assert.That(file.TierOf("btn", "X"), Is.EqualTo(PropertyTier.Degraded));
            Assert.That(file.DegradedReason("btn", "X"), Does.Contain("X=\"5\t\"").And.Contain("preserved exactly as written"));
            Assert.That(file.TierOf("btn", "Y"), Is.Not.EqualTo(PropertyTier.Degraded), "a readable Y is not frozen");
        });
    }

    [TestCase("Y")]
    [TestCase("Width")]
    [TestCase("Height")]
    [TestCase("TabIndex")]
    public void EveryPixelGeometryAttributeAndTheTabIndex_IsJudged(string attribute)
    {
        var attributes = new Dictionary<string, string>
        {
            ["X"] = "5", ["Y"] = "10", ["Width"] = "75", ["Height"] = "23", ["TabIndex"] = "0"
        };
        attributes[attribute] = "two";

        var file = ReadWin(string.Join(" ", attributes.Select(kv => $"{kv.Key}=\"{kv.Value}\"")));

        Assert.That(file.DegradedReason("btn", attribute), Does.Contain($"{attribute}=\"two\""));
    }

    [Test]
    public void AUnicodeMinus_IsDegraded_AndTheReasonNamesIt()
    {
        var file = ReadWin($"X=\"{UnicodeMinus}5\" Y=\"10\" Width=\"75\" Height=\"23\" TabIndex=\"0\"");

        Assert.That(file.DegradedReason("btn", "X"), Does.Contain("U+2212"),
            "a legacy sv-SE document's minus is the one unreadable number we can name the fix for");
    }

    [Test]
    public void AReadableDocument_HasNoDegradedGeometry()
    {
        var file = ReadWin("X=\"-5\" Y=\" 10 \" Width=\"075\" Height=\"23\" TabIndex=\"0\"");

        Assert.That(file.Degraded, Is.Empty, "an ASCII sign, spaces and leading zeros all read (TryParseInt)");
    }

    [Test]
    public void AGridPagesUnreadableCell_IsDegraded()
    {
        var file = FormDocumentReader.Read("F.blwebform", """
            <WebForm Name="F" Version="1">
              <Layout Kind="Grid" Cols="1fr,1fr" Rows="auto"/>
              <Controls>
                <Button Id="btn" Col="one" Row="0" ColSpan="2x" TabIndex="0"/>
              </Controls>
            </WebForm>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(file.DegradedReason("btn", "Col"), Does.Contain("Col=\"one\""));
            Assert.That(file.DegradedReason("btn", "ColSpan"), Does.Contain("ColSpan=\"2x\""));
            Assert.That(file.DegradedReason("btn", "Row"), Is.Null);
        });
    }

    [Test]
    public void ANoOpSave_KeepsTheUnreadableText()
    {
        var xml = WinForm(TabbedX + " Y=\"10\" Width=\"75\" Height=\"23\" TabIndex=\"two\"");

        Assert.That(FormDocumentWriter.Write(FormDocumentReader.Read("F.blform", xml)), Is.EqualTo(xml));
    }

    [Test]
    public void DesignCheck_ReportsADegradedCoordinate()
    {
        var findings = DesignCheck.CheckFormDocument("F.blform", WinForm("X=\"five\" Y=\"10\" Width=\"75\" Height=\"23\""));

        Assert.That(findings.Where(f => f.Code == DesignCodes.DegradedProperty).Select(f => f.Message),
            Has.Some.Contains("'btn.X'"));
    }

    /// <summary>
    /// Found while anchoring: <c>design --check</c> listed control Degraded rows but never the FORM's, so a
    /// Degraded ClientSize or MobileBreakpoint was invisible to it.
    /// </summary>
    [Test]
    public void DesignCheck_ReportsADegradedFormRow()
    {
        var findings = DesignCheck.CheckFormDocument("F.blform", """
            <Form Name="F" Version="1" Width="12px" Height="300"><Controls/></Form>
            """);

        Assert.That(findings.Where(f => f.Code == DesignCodes.DegradedProperty).Select(f => f.Message),
            Has.Some.Contains("'form.ClientSize'"));
    }

    [Test]
    public void TheGridsIntrinsicRow_IsFrozen_ShowingTheDocumentsText()
    {
        var file = ReadWin(TabbedX + " Y=\"10\" Width=\"75\" Height=\"23\" TabIndex=\"0\"");
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = file.Model.FindById("btn");

        var x = grid.AllRows().Single(r => r.Name == "X");
        var y = grid.AllRows().Single(r => r.Name == "Y");
        var location = grid.Rows.Single(r => r.Name == "Location");

        Assert.Multiple(() =>
        {
            Assert.That(location.IsFrozen, Is.True, "the composite over a Degraded part is frozen too (task 6)");
            Assert.That(x.IsFrozen, Is.True);
            Assert.That(x.StringValue, Is.EqualTo("5\t"), "a frozen row shows the document's own text (slice 2 B1)");
            Assert.That(x.FrozenReason, Does.Contain("preserved exactly as written"));
            Assert.That(y.IsFrozen, Is.False);
        });
    }
}
