using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 4 Task 6 / ADR 0020: a ComboBox / ListBox / CheckedListBox item list is stored as <c>&lt;Item&gt;</c> children,
/// one per item; the model holds ONE encoding (LF-joined); a legacy <c>Items="a, b"</c> loads with the old comma rule and a
/// no-op save leaves it byte-identical.
/// </summary>
[TestFixture]
public class FormItemsStorageTests
{
    private static string Win(string comboAttributes = "", string comboChildren = "") =>
        "<Form Name=\"F\" Version=\"1\" Width=\"400\" Height=\"300\">\n" +
        "  <Controls>\n" +
        (comboChildren.Length == 0
            ? $"    <ComboBox Id=\"cmb\" X=\"16\" Y=\"16\" Width=\"121\" Height=\"23\" TabIndex=\"0\"{comboAttributes} />\n"
            : $"    <ComboBox Id=\"cmb\" X=\"16\" Y=\"16\" Width=\"121\" Height=\"23\" TabIndex=\"0\"{comboAttributes}>\n{comboChildren}    </ComboBox>\n") +
        "  </Controls>\n" +
        "</Form>";

    private static string Web(string comboAttributes = "", string comboChildren = "") =>
        "<WebForm Name=\"F\" Version=\"1\">\n" +
        "  <Controls>\n" +
        (comboChildren.Length == 0
            ? $"    <ComboBox Id=\"cmb\" Col=\"0\" Row=\"0\" TabIndex=\"0\"{comboAttributes} />\n"
            : $"    <ComboBox Id=\"cmb\" Col=\"0\" Row=\"0\" TabIndex=\"0\"{comboAttributes}>\n{comboChildren}    </ComboBox>\n") +
        "  </Controls>\n" +
        "</WebForm>";

    private static FormFile Read(string xml, string file = "F.blform")
    {
        var form = FormDocumentReader.Read(file, xml);
        Assert.That(form.IsRefused, Is.False, string.Join("; ", form.Diagnostics.Select(d => d.Format())));
        return form;
    }

    private static FormControl Combo(FormFile file) => file.Model.FindById("cmb")!;

    private static string Code(FormDocument model) =>
        RegionWriter.Write("F.bas", FormScaffolder.Create("F", FormTarget.WinForms).CodeText, model, "F.blform").Text;

    // ==================================================================
    // Legacy attribute
    // ==================================================================

    [TestCase("F.blform")]
    [TestCase("F.blwebform")]
    public void ALegacyAttribute_LoadsAsItsItems_AndANoOpSaveIsByteIdentical(string file)
    {
        var xml = file == "F.blform" ? Win(" Items=\"Alpha, Beta\"") : Web(" Items=\"Alpha, Beta\"");
        var form = Read(xml, file);

        Assert.Multiple(() =>
        {
            Assert.That(Combo(form).Properties["Items"], Is.EqualTo("Alpha\nBeta"), "the old comma rule, one encoding");
            Assert.That(FormDocumentWriter.Write(form), Is.EqualTo(xml), "a no-op save never rewrites a legacy file");
        });
    }

    // ==================================================================
    // Item children
    // ==================================================================

    [Test]
    public void AnEdit_WritesOneItemChildEach_DropsTheAttribute_AndReadsBackTheSame()
    {
        var form = Read(Win(" Items=\"Alpha, Beta\""));
        Combo(form).Properties["Items"] = "Smith, John\nBeta";

        var written = FormDocumentWriter.Write(form);
        var again = Read(written);

        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("<Item>Smith, John</Item>").And.Contain("<Item>Beta</Item>"));
            Assert.That(written, Does.Not.Contain("Items="), "the stale attribute is gone");
            Assert.That(Combo(again).Properties["Items"], Is.EqualTo("Smith, John\nBeta"), "a comma inside an item survives");
            Assert.That(FormDocumentWriter.Write(again), Is.EqualTo(written), "and a no-op save of it is byte-identical");
        });
    }

    [Test]
    public void AmpersandAndLessThan_AreEscapedByXml_ReadBackExactly_AndEmitOnBothTargets()
    {
        var form = Read(Win());
        Combo(form).Properties["Items"] = "A & B\nx < y";

        var written = FormDocumentWriter.Write(form);
        var again = Read(written);
        var web = new FormDocument { Target = FormTarget.Web, Name = "F" };
        var webCombo = new FormControl { Kind = "ComboBox", Id = "cmb", TabIndex = 0 };
        webCombo.Properties["Items"] = Combo(again).Properties["Items"];
        web.Controls.Add(webCombo);
        var html = FormAssetEmitter.Html(web, "App.js");

        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("<Item>A &amp; B</Item>").And.Contain("<Item>x &lt; y</Item>"));
            Assert.That(Combo(again).Properties["Items"], Is.EqualTo("A & B\nx < y"));
            Assert.That(Code(again.Model), Does.Contain("cmb.Items.Add(\"A & B\")").And.Contain("cmb.Items.Add(\"x < y\")"));
            Assert.That(html, Does.Contain("<option>A &amp; B</option>").And.Contain("<option>x &lt; y</option>"));
        });
    }

    [Test]
    public void SpacesAroundAnItem_AreKept()
    {
        var form = Read(Win());
        Combo(form).Properties["Items"] = "  padded \nBeta";

        var again = Read(FormDocumentWriter.Write(form));

        Assert.That(FormItems.Split(Combo(again).Properties["Items"]), Is.EqualTo(new[] { "  padded ", "Beta" }));
    }

    [Test]
    public void EmptyAndWhitespaceLines_AreDropped_OnEveryRoute()
    {
        var form = Read(Win());
        Combo(form).Properties["Items"] = "A\n\n   \nB\r\n";

        var written = FormDocumentWriter.Write(form);

        Assert.Multiple(() =>
        {
            Assert.That(FormItems.Split("A\n\n   \nB\r\n"), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(written.Split("<Item>").Length - 1, Is.EqualTo(2), "two items written");
            Assert.That(Code(form.Model).Split(".Items.Add(").Length - 1, Is.EqualTo(2), "two items emitted");
        });
    }

    /// <summary>⛔ The writer compares the list AS READ (blanks dropped), never element counts.</summary>
    [Test]
    public void BlankItemElements_SaveByteIdentical_EmitNothing_AndAnEditDropsThem()
    {
        var xml = Win(comboChildren: "      <Item>A</Item>\n      <Item/>\n      <Item>  </Item>\n      <Item>B</Item>\n");
        var form = Read(xml);

        Assert.Multiple(() =>
        {
            Assert.That(FormDocumentWriter.Write(form), Is.EqualTo(xml), "no-op save: byte-identical");
            Assert.That(FormItems.Split(Combo(form).Properties["Items"]), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(Code(form.Model).Split(".Items.Add(").Length - 1, Is.EqualTo(2), "nothing blank emitted");
        });

        Combo(form).Properties["Items"] = "A\nB\nC";
        var edited = FormDocumentWriter.Write(form);
        Assert.Multiple(() =>
        {
            Assert.That(edited, Does.Not.Contain("<Item />").And.Not.Contain("<Item/>").And.Not.Contain("<Item>  </Item>"));
            Assert.That(edited.Split("<Item>").Length - 1, Is.EqualTo(3));
        });
    }

    [Test]
    public void RemovingItems_RemovesBothForms()
    {
        var legacy = Read(Win(" Items=\"Alpha, Beta\""));
        var children = Read(Win(comboChildren: "      <Item>A</Item>\n"));
        Combo(legacy).Properties.Remove("Items");
        Combo(children).Properties.Remove("Items");

        Assert.Multiple(() =>
        {
            Assert.That(FormDocumentWriter.Write(legacy), Does.Not.Contain("Items"));
            Assert.That(FormDocumentWriter.Write(children), Does.Not.Contain("<Item"));
        });
    }

    [Test]
    public void Create_WritesItemChildren_BeforeTheBinds_AndNeverTheAttribute()
    {
        var form = Read(Win(comboChildren: "      <Bind Event=\"SelectedIndexChanged\" Handler=\"cmb_Changed\" />\n"));
        Combo(form).Properties["Items"] = "Smith, John\nBeta";

        var created = FormDocumentWriter.Create(form.Model);

        Assert.Multiple(() =>
        {
            Assert.That(created, Does.Not.Contain("Items="));
            Assert.That(created.IndexOf("<Item>Smith, John</Item>", StringComparison.Ordinal),
                Is.GreaterThan(0).And.LessThan(created.IndexOf("<Bind", StringComparison.Ordinal)), "Items first, then the Bind");
        });
    }

    // ==================================================================
    // Degraded: a line break inside an item, or the attribute AND children
    // ==================================================================

    private static readonly string LineBreakItem = Win(comboChildren: "      <Item>a&#10;b</Item>\n      <Item>c</Item>\n");
    private static readonly string BothForms = Win(" Items=\"x, y\"", "      <Item>c</Item>\n");

    private static IEnumerable<TestCaseData> DegradedShapes()
    {
        yield return new TestCaseData(LineBreakItem).SetName("{m}(a line break in an item)");
        yield return new TestCaseData(BothForms).SetName("{m}(the attribute and children)");
    }

    [TestCaseSource(nameof(DegradedShapes))]
    public void ADegradedItemList_IsFrozen_PreservedByteForByte_AndNothingIsEmitted(string xml)
    {
        var form = Read(xml);

        Assert.Multiple(() =>
        {
            Assert.That(form.DegradedReason("cmb", "Items"), Is.Not.Null, "Degraded, with a reason");
            Assert.That(Combo(form).Properties.ContainsKey("Items"), Is.False, "never in the model, so never emitted");
            Assert.That(Combo(form).UnknownChildren.Count(e => e.Name.LocalName == "Item"), Is.GreaterThan(0), "the raw <Item>s");
            Assert.That(FormDocumentWriter.Write(form), Is.EqualTo(xml), "byte-identical");
            Assert.That(Code(form.Model), Does.Not.Contain("Items.Add"));
        });
    }

    /// <summary>The raw content survives every path that carries unknown content: Apply, Create, clone, retarget, clipboard.</summary>
    [TestCaseSource(nameof(DegradedShapes))]
    public void ADegradedItemList_SurvivesEveryPath(string xml)
    {
        var form = Read(xml);
        var combo = Combo(form);
        var hadAttribute = combo.UnknownAttributes.ContainsKey("Items");

        combo.Properties["Text"] = "edited"; // an unrelated edit: the Apply path
        var applied = FormDocumentWriter.Write(form);
        var created = FormDocumentWriter.Create(form.Model);
        var clone = combo.Clone();
        var retargeted = FormRetarget.Convert(form.Model, FormTarget.Web).Document.FindById("cmb")!;
        var pasted = FormClipboard.DeserializeSubtree(
            FormClipboard.SerializeSubtree(FormTarget.WinForms, new[] { combo }), FormTarget.WinForms, _ => false).Single();

        bool CarriesItems(FormControl c) =>
            c.UnknownChildren.Any(e => e.Name.LocalName == "Item") &&
            (!hadAttribute || c.UnknownAttributes.ContainsKey("Items"));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Does.Contain("<Item>c</Item>"), "Apply");
            Assert.That(Read(created).DegradedReason("cmb", "Items"), Is.Not.Null, "Create — and it reads back Degraded");
            Assert.That(CarriesItems(clone), Is.True, "clone");
            Assert.That(CarriesItems(retargeted), Is.True, "retarget");
            Assert.That(CarriesItems(pasted), Is.True, "clipboard");
        });
    }

    // ==================================================================
    // Part E — the layout of a rewritten run, byte-exact
    // ==================================================================

    private const string Open = "    <ComboBox Id=\"cmb\" X=\"16\" Y=\"16\" Width=\"121\" Height=\"23\" TabIndex=\"0\">\n";
    private const string Head = "<Form Name=\"F\" Version=\"1\" Width=\"400\" Height=\"300\">\n  <Controls>\n";
    private const string Tail = "  </Controls>\n</Form>";

    [Test]
    public void AnEdit_OfASelfClosingCombo_LaysTheItemsOutOnePerLine_AtTheDocumentsIndent()
    {
        var form = Read(Win());
        Combo(form).Properties["Items"] = "A\nB";

        Assert.That(FormDocumentWriter.Write(form), Is.EqualTo(
            Head + Open + "      <Item>A</Item>\n      <Item>B</Item>\n    </ComboBox>\n" + Tail));
    }

    [Test]
    public void AnEdit_BeforeABind_PutsEachItemOnItsOwnLine_AndTheBindKeepsItsIndent()
    {
        var form = Read(Win(comboChildren: "      <Bind Event=\"SelectedIndexChanged\" Handler=\"h\" />\n"));
        Combo(form).Properties["Items"] = "A\nB";

        Assert.That(FormDocumentWriter.Write(form), Is.EqualTo(
            Head + Open + "      <Item>A</Item>\n      <Item>B</Item>\n      <Bind Event=\"SelectedIndexChanged\" Handler=\"h\" />\n" +
            "    </ComboBox>\n" + Tail));
    }

    /// <summary>
    /// Part F: the first remaining child sits INLINE, on the start tag's own line. The run still starts on a line of its
    /// own, one step deeper than the element, each item on its own line — and the child follows on a fresh line at that
    /// indent rather than being glued to the last item.
    /// </summary>
    [Test]
    public void AnEdit_BeforeAnInlineFirstChild_StartsTheRunOnItsOwnLine_AndTheChildFollowsOnItsOwn()
    {
        const string inlineOpen = "    <ComboBox Id=\"cmb\" X=\"16\" Y=\"16\" Width=\"121\" Height=\"23\" TabIndex=\"0\">";
        const string bind = "<Bind Event=\"SelectedIndexChanged\" Handler=\"h\" />";
        var form = Read(Head + inlineOpen + bind + "</ComboBox>\n" + Tail);
        Combo(form).Properties["Items"] = "A\nB";

        var written = FormDocumentWriter.Write(form);

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.EqualTo(
                Head + inlineOpen + "\n      <Item>A</Item>\n      <Item>B</Item>\n      " + bind + "</ComboBox>\n" + Tail));
            Assert.That(FormItems.Split(Combo(Read(written)).Properties["Items"]), Is.EqualTo(new[] { "A", "B" }),
                "and it reads back the same");
        });
    }

    /// <summary>Two consecutive edits: the second has exactly the first's shape — no blank line grows per save.</summary>
    [TestCase("")]
    [TestCase("      <Bind Event=\"SelectedIndexChanged\" Handler=\"h\" />\n")]
    public void TwoConsecutiveEdits_KeepTheSameShape(string bind)
    {
        var form = Read(Win(comboChildren: bind));
        Combo(form).Properties["Items"] = "A\nB";
        FormDocumentWriter.Write(form);
        Combo(form).Properties["Items"] = "C\nD\nE";

        Assert.That(FormDocumentWriter.Write(form), Is.EqualTo(
            Head + Open + "      <Item>C</Item>\n      <Item>D</Item>\n      <Item>E</Item>\n" + bind + "    </ComboBox>\n" + Tail));
    }

    [Test]
    public void RemovingEveryItem_LeavesNoBlankLine()
    {
        var form = Read(Win(comboChildren: "      <Item>A</Item>\n"));
        Combo(form).Properties.Remove("Items");

        var written = FormDocumentWriter.Write(form);

        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Not.Contain("<Item"));
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(written, "\n[ \t]*\n"), Is.False, $"a whitespace-only line:\n{written}");
        });
    }

    [Test]
    public void ATabIndentedFile_StaysTabIndented()
    {
        var xml = "<Form Name=\"F\" Version=\"1\" Width=\"400\" Height=\"300\">\n\t<Controls>\n" +
                  "\t\t<ComboBox Id=\"cmb\" X=\"16\" Y=\"16\" Width=\"121\" Height=\"23\" TabIndex=\"0\" />\n\t</Controls>\n</Form>";
        var form = Read(xml);
        Combo(form).Properties["Items"] = "A\nB";

        Assert.That(FormDocumentWriter.Write(form), Is.EqualTo(
            "<Form Name=\"F\" Version=\"1\" Width=\"400\" Height=\"300\">\n\t<Controls>\n" +
            "\t\t<ComboBox Id=\"cmb\" X=\"16\" Y=\"16\" Width=\"121\" Height=\"23\" TabIndex=\"0\">\n" +
            "\t\t\t<Item>A</Item>\n\t\t\t<Item>B</Item>\n\t\t</ComboBox>\n\t</Controls>\n</Form>"));
    }

    [Test]
    public void ACrlfFile_StaysCrlf()
    {
        var form = Read(Win().Replace("\n", "\r\n"));
        Combo(form).Properties["Items"] = "A\nB";

        Assert.That(FormDocumentWriter.Write(form), Is.EqualTo(
            (Head + Open + "      <Item>A</Item>\n      <Item>B</Item>\n    </ComboBox>\n" + Tail).Replace("\n", "\r\n")));
    }

    // ==================================================================
    // Part E — an <Item> with attributes or markup is Degraded, never flattened
    // ==================================================================

    [TestCase("      <Item Value=\"1\">A</Item>\n")]
    [TestCase("      <Item>A<b>bold</b></Item>\n")]
    public void AnItemWithAttributesOrMarkup_IsDegraded_AndPreservedByteForByte(string children)
    {
        var xml = Win(comboChildren: children);
        var form = Read(xml);

        Assert.Multiple(() =>
        {
            Assert.That(form.DegradedReason("cmb", "Items"), Does.Contain("attributes or markup"));
            Assert.That(Combo(form).Properties.ContainsKey("Items"), Is.False);
            Assert.That(FormDocumentWriter.Write(form), Is.EqualTo(xml));
            Assert.That(Code(form.Model), Does.Not.Contain("Items.Add"));
        });
    }

    /// <summary>Part E: a clipboard fragment with a LEGACY comma attribute reads with the old rule, as a document does.</summary>
    [Test]
    public void TheClipboard_ReadsALegacyCommaPayloadWithTheOldRule()
    {
        const string fragment = """
            <FormSubtree Target="WinForms" Version="1">
              <ComboBox Id="cmb" X="0" Y="0" Width="10" Height="10" TabIndex="0" Items="Alpha, Beta" />
            </FormSubtree>
            """;

        var pasted = FormClipboard.DeserializeSubtree(fragment, FormTarget.WinForms, _ => false).Single();

        Assert.That(pasted.Properties["Items"], Is.EqualTo("Alpha\nBeta"));
    }

    // ==================================================================
    // Clipboard, retarget, emission
    // ==================================================================

    [Test]
    public void TheClipboard_RoundTripsACommaItem()
    {
        var form = Read(Win());
        Combo(form).Properties["Items"] = "Smith, John\nBeta";

        var pasted = FormClipboard.DeserializeSubtree(
            FormClipboard.SerializeSubtree(FormTarget.WinForms, new[] { Combo(form) }), FormTarget.WinForms, _ => false).Single();

        Assert.That(pasted.Properties["Items"], Is.EqualTo("Smith, John\nBeta"));
    }

    [Test]
    public void AWebRetarget_CarriesTheItems_AndThePageHasOneOptionEach()
    {
        var form = Read(Win());
        Combo(form).Properties["Items"] = "Smith, John\nBeta";

        var web = FormRetarget.Convert(form.Model, FormTarget.Web).Document;
        var html = FormAssetEmitter.Html(web, "App.js");

        Assert.Multiple(() =>
        {
            Assert.That(web.FindById("cmb")!.Properties["Items"], Is.EqualTo("Smith, John\nBeta"));
            Assert.That(html, Does.Contain("<option>Smith, John</option>").And.Contain("<option>Beta</option>"));
        });
    }

    [Test]
    public void TheRegionWriter_AddsEachItem_CommaIncluded()
    {
        var form = Read(Win());
        Combo(form).Properties["Items"] = "Smith, John\nBeta";

        Assert.That(Code(form.Model), Does.Contain("cmb.Items.Add(\"Smith, John\")").And.Contain("cmb.Items.Add(\"Beta\")"));
    }
}
