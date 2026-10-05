using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.ViewModels.Designer;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 6 Task 3 (D-4/D-5/D-9): the multi-edit. ONE <c>Edited</c> per gesture, all-or-nothing, each member judged on its
/// own value, parts composed per member, the mixed editors write nothing by accident — and, through the REAL document view
/// model, one undo step that restores every member.
/// </summary>
public partial class FormPropertyGridMultiSelectTests
{
    /// <summary>A live count of <see cref="FormPropertyGridViewModel.Edited"/>.</summary>
    private static Func<int> Edits(FormPropertyGridViewModel grid)
    {
        var edits = 0;
        grid.Edited += (_, _) => edits++;
        return () => edits;
    }

    private static FormPropertyRow Part(FormPropertyGridViewModel grid, string name) =>
        grid.AllRows().Single(r => r.Name == name);

    /// <summary>The REAL document view model on <paramref name="doc"/>, in Design view, with <paramref name="select"/> selected.</summary>
    private static CodeEditorDocumentViewModel OpenVm(string doc, params string[] select)
    {
        var scaffold = FormScaffolder.Create("GridForm", FormTarget.WinForms);
        var contents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/proj/" + scaffold.DocumentFileName] = doc,
            ["/proj/" + scaffold.CodeFileName] = scaffold.CodeText
        };
        var files = new Mock<IFileService>();
        files.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string p, CancellationToken _) => Task.FromResult(contents[p]));
        files.Setup(f => f.FileExistsAsync(It.IsAny<string>())).Returns((string p) => Task.FromResult(contents.ContainsKey(p)));
        files.Setup(f => f.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string p, string text, CancellationToken _) =>
            {
                contents[p] = text;
                return Task.CompletedTask;
            });

        var vm = new CodeEditorDocumentViewModel(files.Object, new Mock<IEventAggregator>().Object)
        {
            FilePath = "/proj/" + scaffold.DocumentFileName
        };
        vm.SetContent(doc);
        Assert.That(vm.EnterDesignModeForFormDocument(), Is.True, "precondition: the designer opens");
        vm.Selection.SetRange(select.Select(id => vm.DesignDocument!.FindById(id)!).ToList());
        Assert.That(vm.PropertyGrid.SelectedControls.Count, Is.EqualTo(select.Length), "precondition: the grid holds the set");
        return vm;
    }

    // ==================================================================
    // ONE Edited, every member holds the value
    // ==================================================================

    /// <summary>A value this row's EVERY member would WRITE (never a no-op, a reset or a refusal) — or null.</summary>
    private static string? WritableEverywhere(FormPropertyRow row)
    {
        var definition = row.Members[^1].Definition;
        IEnumerable<string> candidates = row.Name switch
        {
            "Location" => new[] { "11, 12" },
            "Size" when definition == null => new[] { "61, 33" },
            "Anchor" => new[] { "Top,Right" },
            "Dock" => new[] { "Fill" },
            _ => definition?.Type switch
            {
                FormPropertyType.Enum => definition.AllowedValues ?? Array.Empty<string>(),
                FormPropertyType.Cursor => definition.Choices ?? Array.Empty<string>(),
                FormPropertyType.Bool => new[] { "True", "False" },
                FormPropertyType.Color => new[] { "Red", "Blue", "Green" },
                FormPropertyType.Int => new[] { "7", "3" },
                FormPropertyType.Font => new[] { "Arial, 12pt", "Tahoma, 11pt" },
                FormPropertyType.Padding => new[] { "3", "5" },
                FormPropertyType.Size => new[] { "61, 33" },
                FormPropertyType.Image => new[] { "Resources/a.png" },
                FormPropertyType.Fraction => new[] { "0.5" },
                _ => new[] { "Zed", "Yak" }
            }
        };

        return candidates.FirstOrDefault(c => row.Members.All(m =>
            m.Definition == null
                ? !string.Equals(m.DisplayValue, c, StringComparison.Ordinal)
                : m.Definition.Judge(c, m.IsPresent ? m.RawValue : null, FormTarget.WinForms) == FormEditVerdict.Write));
    }

    /// <summary>
    /// ⛔ Catalog-driven over EVERY row {btn, btn2, lbl} shares: a valid value every member would write raises Edited
    /// EXACTLY ONCE, and every member then holds it.
    /// </summary>
    [Test]
    public void EverySharedRow_OfTwoButtonsAndALabel_WritesEveryMember_WithOneEdited()
    {
        var (_, grid) = Open("btn", "btn2", "lbl");
        var edits = Edits(grid);
        var findings = new List<string>();
        var exercised = 0;

        foreach (var row in grid.Rows.ToList())
        {
            var value = WritableEverywhere(row);
            if (value == null)
            {
                findings.Add($"{row.Name}: no value every member would write — extend the candidates");
                continue;
            }

            var before = edits();
            row.StringValue = value;
            exercised++;

            if (edits() - before != 1)
            {
                findings.Add($"{row.Name} = {value}: Edited raised {edits() - before} times");
            }

            var stale = row.Members.Where(m => m.Definition == null
                    ? !string.Equals(m.DisplayValue, value, StringComparison.Ordinal)
                    : !m.Definition.SameValue(m.DisplayValue, value))
                .Select(m => m.DisplayValue).ToList();
            if (stale.Count > 0)
            {
                findings.Add($"{row.Name} = {value}: members still show [{string.Join(", ", stale)}]");
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(exercised, Is.GreaterThan(10), "precondition: the sweep exercised the shared rows");
            Assert.That(findings, Is.Empty, string.Join("\n", findings));
        });
    }

    [TestCase("Text")]
    [TestCase("Bool")]
    [TestCase("Enum")]
    [TestCase("Color")]
    [TestCase("FontWhole")]
    [TestCase("FontBoldPart")]
    [TestCase("PaddingAllPart")]
    [TestCase("SizeWidthPart")]
    [TestCase("LocationX")]
    [TestCase("Anchor")]
    [TestCase("Dock")]
    [TestCase("BoolDoubleClick")]
    public void EachKindOfMultiEdit_RaisesEditedExactlyOnce(string gesture)
    {
        var (file, grid) = Open("btn", "btn2", "lbl");
        var edits = Edits(grid);
        var ids = new[] { "btn", "btn2", "lbl" };
        string Attr(string id, string name) => file.Model.FindById(id)!.Properties.GetValueOrDefault(name) ?? "";
        PixelGeometry Pixel(string id) => (PixelGeometry)file.Model.FindById(id)!.Geometry!;

        Action check;
        switch (gesture)
        {
            case "Text":
                Row(grid, "Text").StringValue = "Go";
                check = () => Assert.That(ids.Select(i => Attr(i, "Text")), Is.All.EqualTo("Go"));
                break;
            case "Bool":
                Row(grid, "Enabled").BoolValue = false;
                check = () => Assert.That(ids.Select(i => Attr(i, "Enabled")), Is.All.EqualTo("false"));
                break;
            case "Enum":
                Row(grid, "TextAlign").StringValue = "TopRight";
                check = () => Assert.That(ids.Select(i => Attr(i, "TextAlign")), Is.All.EqualTo("TopRight"));
                break;
            case "Color":
                Row(grid, "BackColor").ApplyColor("Green");
                check = () => Assert.That(ids.Select(i => Attr(i, "BackColor")), Is.All.EqualTo("Green"));
                break;
            case "FontWhole":
                Row(grid, "Font").ApplyFont("Arial, 12pt");
                check = () => Assert.That(ids.Select(i => Attr(i, "Font")), Is.All.EqualTo("Arial, 12pt"));
                break;
            case "FontBoldPart":
                Part(grid, "Bold").StringValue = "True";
                check = () => Assert.That(ids.Select(i => Attr(i, "Font")), Is.All.Contains("Bold"));
                break;
            case "PaddingAllPart":
                Part(grid, "All").StringValue = "4";
                check = () => Assert.That(ids.Select(i => Attr(i, "Padding")), Is.All.EqualTo("4"));
                break;
            case "SizeWidthPart":
                Part(grid, "Width").StringValue = "90";
                check = () => Assert.That(ids.Select(i => Pixel(i).Width), Is.All.EqualTo(90));
                break;
            case "LocationX":
                Part(grid, "X").StringValue = "96";
                check = () => Assert.That(ids.Select(i => Pixel(i).X), Is.All.EqualTo(96));
                break;
            case "Anchor":
                Row(grid, "Anchor").AnchorRight = true;
                check = () => Assert.That(ids.Select(i => Pixel(i).Anchor), Is.All.EqualTo("Top,Left,Right"));
                break;
            case "Dock":
                Row(grid, "Dock").SetDock("Fill");
                check = () => Assert.That(ids.Select(i => Pixel(i).Dock), Is.All.EqualTo("Fill"));
                break;
            case "BoolDoubleClick":
                file.Model.FindById("btn")!.Properties["Visible"] = "false"; // Visible mixed: false, (true), (true)
                grid.SetSelection(Array.Empty<FormControl>());
                grid.SetSelection(ids.Select(i => file.Model.FindById(i)!).ToList());
                Assert.That(Row(grid, "Visible").IsMixed, Is.True, "precondition: Visible is mixed");
                var before = edits();
                Row(grid, "Visible").ToggleBool();
                Assert.That(edits() - before, Is.EqualTo(1), "D-4: a mixed Bool's double-click sets True on all, ONE Edited");
                Assert.That(Row(grid, "Visible").IsMixed, Is.False);
                Assert.That(Row(grid, "Visible").Members.Select(m => m.DisplayValue), Is.All.EqualTo("true"));
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(gesture));
        }

        Assert.Multiple(() =>
        {
            Assert.That(edits(), Is.EqualTo(1), $"{gesture}: ONE Edited for the whole selection");
            check();
        });
    }

    /// <summary>D-5: Reset on a merged row whose members can ALL reset acts — every member loses the attribute, ONE Edited.</summary>
    [Test]
    public void ResetOnAMergedRow_RemovesTheAttributeFromEveryMember_WithOneEdited()
    {
        var (file, grid) = Open("btn", "btn2");
        var edits = Edits(grid);
        var backColor = Row(grid, "BackColor");
        Assert.That(backColor.CanReset, Is.True, "precondition: both carry BackColor");

        backColor.ResetCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(edits(), Is.EqualTo(1));
            Assert.That(file.Model.FindById("btn")!.Properties.ContainsKey("BackColor"), Is.False);
            Assert.That(file.Model.FindById("btn2")!.Properties.ContainsKey("BackColor"), Is.False);
            Assert.That(backColor.IsDefaultShown, Is.True, "both absent now: the shared default, grey");
            Assert.That(backColor.IsEditable, Is.True, "a merged row is editable (the Task-2 interim freeze is gone)");
        });
    }

    /// <summary>Review of 9fe0d153 (5): a successful Reset retracts the refusal standing on the merged row.</summary>
    [Test]
    public void ASuccessfulReset_RetractsTheMergedRowsRefusal()
    {
        var (_, grid) = Open("btn", "btn2");
        var backColor = Row(grid, "BackColor");
        backColor.ApplyColor("Bogus");
        Assert.That(backColor.Refusal, Is.Not.Null, "precondition: refused");

        backColor.ResetCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(backColor.Refusal, Is.Null);
            Assert.That(grid.DescriptionBody, Does.Not.Contain("'Bogus'"), "the pane no longer says it");
        });
    }

    /// <summary>
    /// Review of 9fe0d153 (2): all-or-nothing covers the STORE, not only the catalog. Two stored-value members over one
    /// catalog row (the Form's ClientSize, whose value the catalog accepts), the second member's store refusing: the FIRST
    /// member must not be written, no Edited, and the refusing member's reason is said on the merged row.
    /// </summary>
    [Test]
    public void AValueOneMembersStoreRefuses_WritesNoMember_AndTheMergedRowSaysWhy()
    {
        var definition = FormControlCatalog.FormRoot.Property("ClientSize")!;
        var tally = new FormEditTally();
        string? storeA = null;
        var a = FormPropertyRow.ForStoredValue(definition, FormTarget.WinForms,
            read: () => storeA, write: v => { storeA = v; return true; }, remove: () => storeA = null, onChanged: tally.Mark,
            storeRefusal: _ => null);
        var b = FormPropertyRow.ForStoredValue(definition, FormTarget.WinForms,
            read: () => null, write: _ => false, remove: null, onChanged: tally.Mark,
            storeRefusal: v => $"'{v}' does not fit this store.");
        var edits = 0;
        var merged = FormPropertyRow.Merged(new[] { a, b },
            new[] { new FormControl { Kind = "Panel", Id = "pa" }, new FormControl { Kind = "Panel", Id = "pb" } }, tally, () => edits++);
        Assert.That(definition.Judge("300, 200", null, FormTarget.WinForms), Is.EqualTo(FormEditVerdict.Write),
            "precondition: the catalog accepts the value — only the store refuses");

        merged.StringValue = "300, 200";

        Assert.Multiple(() =>
        {
            Assert.That(storeA, Is.Null, "the accepting member is NOT written (all-or-nothing)");
            Assert.That(edits, Is.Zero);
            Assert.That(merged.Refusal, Is.EqualTo("'pb' (Panel): '300, 200' does not fit this store."));
        });
    }

    /// <summary>
    /// Review of 9fe0d153 (3): a revision refresh raises each row ONCE — a merged part's member part was raised twice
    /// (through the merged part and again through its member parent). Cost stays ∝ the selection.
    /// </summary>
    [Test]
    public void RefreshValues_RaisesEachMemberPartOnce()
    {
        var (_, grid) = Open("btn", "btn2", "lbl");
        var memberPart = Part(grid, "Bold").Members[0];
        var memberParent = Row(grid, "Font").Members[0];
        var partRaises = 0;
        var parentRaises = 0;
        memberPart.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FormPropertyRow.DisplayValue)) partRaises++;
        };
        memberParent.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FormPropertyRow.DisplayValue)) parentRaises++;
        };

        grid.RefreshValues();

        Assert.Multiple(() =>
        {
            Assert.That(partRaises, Is.EqualTo(1), "a member's part: once");
            Assert.That(parentRaises, Is.EqualTo(1), "a member: once");
        });
    }

    /// <summary>D-3/D-5: one member set and one absent is bold with no Reset — and clearing the editor writes nothing.</summary>
    [Test]
    public void ResetIsNotOffered_WhenAMemberIsAbsent_AndAClearedEditorWritesNothing()
    {
        var (file, grid) = Open("btn", "btn2");
        var edits = Edits(grid);
        var foreColor = Row(grid, "ForeColor"); // Blue on btn, absent on btn2

        foreColor.ResetCommand.Execute(null);
        foreColor.ApplyColor(""); // the cleared editor's Reset verdict on btn

        Assert.Multiple(() =>
        {
            Assert.That(foreColor.CanReset, Is.False);
            Assert.That(edits(), Is.Zero);
            Assert.That(file.Model.FindById("btn")!.Properties["ForeColor"], Is.EqualTo("Blue"), "all-or-nothing");
        });
    }

    // ==================================================================
    // No-op, per-member judging, all-or-nothing
    // ==================================================================

    [Test]
    public void AValueEveryMemberAlreadyShows_RaisesNothing()
    {
        var (_, grid) = Open("btn", "btn2");
        var edits = Edits(grid);

        Row(grid, "Text").StringValue = "OK";
        Row(grid, "BackColor").ApplyColor("red");
        Part(grid, "Width").StringValue = "75";

        Assert.That(edits(), Is.Zero);
    }

    [Test]
    public void EachMemberIsJudgedOnItsOwnValue_AndTheEditIsStillOneEdited()
    {
        var (file, grid) = Open("btn", "lbl"); // btn BackColor=Red, lbl absent
        var edits = Edits(grid);
        var btnBefore = string.Join(";", file.Model.FindById("btn")!.Properties.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));

        Row(grid, "BackColor").ApplyColor("Red");

        Assert.Multiple(() =>
        {
            Assert.That(edits(), Is.EqualTo(1));
            Assert.That(file.Model.FindById("lbl")!.Properties["BackColor"], Is.EqualTo("Red"), "lbl written");
            Assert.That(string.Join(";", file.Model.FindById("btn")!.Properties.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}")),
                Is.EqualTo(btnBefore), "btn already held Red: untouched");
        });
    }

    /// <summary>
    /// ⛔ D-5 all-or-nothing: a translucent BackColor is fine on a Label and THROWS on a WinForms TextBox
    /// (<c>OpaqueOnWinForms</c>), so the set refuses it — neither written, no Edited, and the reason names the TextBox.
    /// </summary>
    [Test]
    public void AValueOneMemberRefuses_IsRefusedForTheSet_AndTheReasonNamesIt()
    {
        var (file, grid) = Open("lbl", "txt");
        var edits = Edits(grid);
        var backColor = Row(grid, "BackColor");
        Assert.That(backColor.Members[0].Definition!.Judge("Transparent", null, FormTarget.WinForms),
            Is.EqualTo(FormEditVerdict.Write), "precondition: the Label alone would take it");

        backColor.ApplyColor("Transparent");

        Assert.Multiple(() =>
        {
            Assert.That(edits(), Is.Zero);
            Assert.That(file.Model.FindById("lbl")!.Properties.ContainsKey("BackColor"), Is.False, "not partially applied");
            Assert.That(file.Model.FindById("txt")!.Properties.ContainsKey("BackColor"), Is.False);
            Assert.That(backColor.Refusal, Does.StartWith("'txt' (TextBox): "));
            Assert.That(grid.DescriptionBody, Is.EqualTo(backColor.Refusal), "the pane says why");
        });
    }

    // ==================================================================
    // Parts and the mixed editors
    // ==================================================================

    [Test]
    public void TheBoldPart_OnTwoDifferentFonts_KeepsEachFamilyAndSize()
    {
        var doc = MultiDoc
            .Replace("""Text="OK" BackColor="Red" ForeColor="Blue"/>""", """Text="OK" BackColor="Red" ForeColor="Blue" Font="Courier New, 12pt"/>""")
            .Replace("""Text="Hello"/>""", """Text="Hello" Font="Arial, 10pt"/>""");
        var (file, grid) = OpenDoc(doc, "btn", "lbl");
        var edits = Edits(grid);
        Assert.Multiple(() =>
        {
            Assert.That(Row(grid, "Font").IsMixed, Is.True, "precondition: different fonts");
            Assert.That(Part(grid, "Bold").DisplayValue, Is.EqualTo("false"), "both not bold: the part is shared");
        });

        Part(grid, "Bold").StringValue = "True";

        Assert.Multiple(() =>
        {
            Assert.That(edits(), Is.EqualTo(1));
            Assert.That(file.Model.FindById("btn")!.Properties["Font"], Does.StartWith("Courier New, 12pt"));
            Assert.That(file.Model.FindById("lbl")!.Properties["Font"], Does.StartWith("Arial, 10pt"));
            Assert.That(file.Model.FindById("btn")!.Properties["Font"], Does.Contain("Bold"));
            Assert.That(file.Model.FindById("lbl")!.Properties["Font"], Does.Contain("Bold"));
        });
    }

    [Test]
    public void AMixedIntRow_IsATextBox_AndBlankOrUnparseableTextWritesNothing()
    {
        var (file, grid) = Open("btn", "lbl"); // Width 75 vs 100
        var edits = Edits(grid);
        var width = Part(grid, "Width");

        Assert.Multiple(() =>
        {
            Assert.That(width.IsMixed, Is.True);
            Assert.That(width.IsNumericUpDown, Is.False, "a NumericUpDown would show a false 0 (M5)");
            Assert.That(width.IsTextBox, Is.True);
            Assert.That(width.StringValue, Is.Empty);
        });

        width.StringValue = "";
        width.StringValue = "abc";

        Assert.Multiple(() =>
        {
            Assert.That(edits(), Is.Zero);
            Assert.That(((PixelGeometry)file.Model.FindById("btn")!.Geometry!).Width, Is.EqualTo(75));
            Assert.That(((PixelGeometry)file.Model.FindById("lbl")!.Geometry!).Width, Is.EqualTo(100));
        });

        width.StringValue = "90";
        Assert.Multiple(() =>
        {
            Assert.That(edits(), Is.EqualTo(1));
            Assert.That(width.IsMixed, Is.False);
            Assert.That(width.IsNumericUpDown, Is.True, "un-mixed: the NumericUpDown again");
            Assert.That(width.IsTextBox, Is.False);
        });
    }

    /// <summary>
    /// D-4 "keyed on the EDITOR, not on today's mixedness": an Int row's StringValue is pushed ONLY by the text box it gets
    /// while mixed. When the members became equal under that box's focus, its LostFocus still pushes the stale "" — into
    /// a row that is no longer mixed. On a catalog Int (MaxLength) that "" is Judge's RESET verdict, so a guard keyed on
    /// today's mixedness would strip the attribute from every member. (Measured in the real view, test (j): Avalonia
    /// repainted the dying box with the new value before its LostFocus, so this VM test is where the rule is pinned.)
    /// </summary>
    [Test]
    public void AStaleEmptyPushIntoAnUnMixedIntRow_WritesNothing()
    {
        var doc = MultiDoc
            .Replace("""<TextBox Id="txt" X="120" Y="56" Width="100" Height="23" TabIndex="3"/>""",
                """<TextBox Id="txt" X="120" Y="56" Width="100" Height="23" TabIndex="3" MaxLength="10"/>""" +
                """<TextBox Id="txt2" X="120" Y="96" Width="100" Height="23" TabIndex="4" MaxLength="10"/>""");
        var (file, grid) = OpenDoc(doc, "txt", "txt2");
        var edits = Edits(grid);
        var maxLength = Row(grid, "MaxLength");
        Assert.Multiple(() =>
        {
            Assert.That(maxLength.IsMixed, Is.False, "precondition: both 10");
            Assert.That(maxLength.CanReset, Is.True, "precondition: a Reset would act");
        });

        maxLength.StringValue = ""; // the dying mixed text box's LostFocus

        Assert.Multiple(() =>
        {
            Assert.That(edits(), Is.Zero);
            Assert.That(file.Model.FindById("txt")!.Properties.GetValueOrDefault("MaxLength"), Is.EqualTo("10"));
            Assert.That(file.Model.FindById("txt2")!.Properties.GetValueOrDefault("MaxLength"), Is.EqualTo("10"));
        });
    }

    /// <summary>D-4 / M4: a mixed Bool or Enum combo shows nothing; a "" or null push from it writes nothing.</summary>
    [Test]
    public void AMixedComboPushOfEmptyOrNull_WritesNothing()
    {
        var doc = MultiDoc.Replace("""Text="OK" BackColor="Red"/>""", """Text="OK" BackColor="Red" Enabled="false" TextAlign="TopRight"/>""");
        var (_, grid) = OpenDoc(doc, "btn", "btn2");
        var edits = Edits(grid);

        foreach (var name in new[] { "Enabled", "TextAlign" })
        {
            var row = Row(grid, name);
            Assert.That(row.IsMixed, Is.True, $"precondition: {name} mixed");
            Assert.That(row.StringValue, Is.Empty, $"{name}: the combo selects nothing");
            row.StringValue = "";
            row.StringValue = null!;
        }

        Assert.That(edits(), Is.Zero);
    }

    [Test]
    public void MixedAnchorAndDock_ShowBlankSummaries_AndTheAnchorBoxStartsAtWinFormsDefault()
    {
        var doc = MultiDoc.Replace("""Text="OK" BackColor="Red"/>""", """Text="OK" BackColor="Red" Anchor="Bottom,Right" Dock="Top"/>""");
        var (file, grid) = OpenDoc(doc, "btn", "btn2");
        var edits = Edits(grid);
        var anchor = Row(grid, "Anchor");
        var dock = Row(grid, "Dock");

        Assert.Multiple(() =>
        {
            Assert.That(anchor.AnchorSummary, Is.Empty);
            Assert.That(anchor.AnchorTop && anchor.AnchorLeft && !anchor.AnchorRight && !anchor.AnchorBottom, Is.True,
                "the box starts from Top, Left (FormAnchor.Parse(\"\"))");
            Assert.That(dock.DockSummary, Is.Empty);
            Assert.That(dock.IsDockedNone || dock.IsDockedTop || dock.IsDockedFill, Is.False, "no region lit");
        });

        anchor.AnchorRight = true; // the WHOLE value Top,Left,Right to both

        Assert.Multiple(() =>
        {
            Assert.That(edits(), Is.EqualTo(1));
            Assert.That(((PixelGeometry)file.Model.FindById("btn")!.Geometry!).Anchor, Is.EqualTo("Top,Left,Right"));
            Assert.That(((PixelGeometry)file.Model.FindById("btn2")!.Geometry!).Anchor, Is.EqualTo("Top,Left,Right"));
        });
    }

    [Test]
    public void TheFontDialog_StartsFromThePrimarysFont_WhenMixed()
    {
        var doc = MultiDoc
            .Replace("""Text="OK" BackColor="Red" ForeColor="Blue"/>""", """Text="OK" BackColor="Red" ForeColor="Blue" Font="Courier New, 12pt"/>""")
            .Replace("""Text="Hello"/>""", """Text="Hello" Font="Arial, 10pt"/>""");
        var (_, grid) = OpenDoc(doc, "btn", "lbl"); // primary lbl

        Assert.That(Row(grid, "Font").EffectiveFont?.Family, Is.EqualTo("Arial"));
    }

    // ==================================================================
    // Through the REAL document view model: one undo step, D-9's refresh, re-entrancy
    // ==================================================================

    [Test]
    public void AMultiEdit_IsOneUndoStep_AndOneUndoRestoresEveryMemberByteForByte()
    {
        var vm = OpenVm(MultiDoc, "btn", "btn2", "lbl");
        var edits = Edits(vm.PropertyGrid);
        var before = vm.Text;

        vm.PropertyGrid.Rows.Single(r => r.Name == "Text").StringValue = "Go";

        Assert.Multiple(() =>
        {
            Assert.That(edits(), Is.EqualTo(1));
            Assert.That(vm.Text, Does.Contain("Id=\"btn\"").And.Not.EqualTo(before));
            Assert.That(vm.DesignDocument!.FindById("lbl")!.Properties["Text"], Is.EqualTo("Go"));
        });

        vm.UndoDesignerEditCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.Text, Is.EqualTo(before), "ONE undo restores all three, byte for byte");
            Assert.That(vm.TextDocument.UndoStack.CanUndo, Is.False, "and it was the only step");
        });
    }

    [Test]
    public void ANoOpMultiEdit_LeavesTheUndoStackEmpty()
    {
        var vm = OpenVm(MultiDoc, "btn", "btn2");

        vm.PropertyGrid.Rows.Single(r => r.Name == "Text").StringValue = "OK";

        Assert.That(vm.TextDocument.UndoStack.CanUndo, Is.False);
    }

    /// <summary>
    /// D-9 (and M2): Arrange while two controls are selected — the merged Location shows the new shared value WITHOUT a
    /// reselect, because the document's revision refreshes the rows' values.
    /// </summary>
    [Test]
    public void AfterAnArrange_TheMergedLocationShowsTheNewValue_WithoutReselecting()
    {
        var vm = OpenVm(MultiDoc, "btn", "lbl"); // X 16 and 120: mixed; primary lbl
        var location = vm.PropertyGrid.Rows.Single(r => r.Name == "Location");
        var x = location.Children.Single(c => c.Name == "X");
        var raised = new List<string?>();
        x.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.That(x.IsMixed, Is.True, "precondition: X differs");

        vm.ArrangeCommand.Execute(FormArrangeKind.AlignLeft);

        Assert.Multiple(() =>
        {
            Assert.That(x.DisplayValue, Is.EqualTo("120"), "aligned to the primary's left");
            Assert.That(raised, Does.Contain(nameof(FormPropertyRow.StringValue)).And.Contain(nameof(FormPropertyRow.IntValue)),
                "the editor was told — it re-reads without a reselect");
            Assert.That(vm.PropertyGrid.Rows.Single(r => r.Name == "Location"), Is.SameAs(location), "no rebuild");
        });
    }

    /// <summary>
    /// D-9 re-entrancy, VM half: an accepted merged edit raises Edited ONCE with the revision refresh running INSIDE it,
    /// and adds exactly ONE undo step; a refused one writes nothing and adds none.
    /// </summary>
    [Test]
    public void TheRevisionRefreshInsideAMergedEdit_AddsNoSecondWriteOrStep()
    {
        var vm = OpenVm(MultiDoc, "lbl", "txt");
        var edits = Edits(vm.PropertyGrid);
        var backColor = vm.PropertyGrid.Rows.Single(r => r.Name == "BackColor");
        var refreshedInside = 0;
        backColor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FormPropertyRow.StringValue)) refreshedInside++;
        };
        var before = vm.Text;

        backColor.ApplyColor("Transparent"); // refused on txt
        Assert.Multiple(() =>
        {
            Assert.That(edits(), Is.Zero);
            Assert.That(vm.Text, Is.EqualTo(before));
            Assert.That(vm.TextDocument.UndoStack.CanUndo, Is.False);
        });

        refreshedInside = 0;
        backColor.ApplyColor("Red");

        Assert.Multiple(() =>
        {
            Assert.That(edits(), Is.EqualTo(1));
            Assert.That(refreshedInside, Is.GreaterThanOrEqualTo(2), "its own raise, and the revision refresh inside Edited");
        });

        vm.UndoDesignerEditCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.Text, Is.EqualTo(before));
            Assert.That(vm.TextDocument.UndoStack.CanUndo, Is.False, "exactly one step");
        });
    }
}
