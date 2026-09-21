using System.Xml.Linq;
using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 24, commit 24c, Task 13 Step 1 — the reader's <c>Place</c> branch and BL8030 (spec §3).
///
/// <para>⛔ RED until Task 13 Step 3 makes <c>ReadControl</c> place-aware. Today (Task 12 landed,
/// Task 13 not yet) every element under <c>&lt;Controls&gt;</c> is read as if it were Positioned,
/// regardless of its catalog row's <see cref="FormPlace"/> — so a strip's <c>Dock</c> lands in
/// <c>ReadGeometry</c> (it is one of <c>ReadGeometry</c>'s six trigger attributes) and never in
/// <c>Properties</c>, and the writer's "a catalog property the model dropped" sweep
/// (<c>FormDocumentWriter.ApplyControl</c>) then DELETES it on the very next save — a strip document
/// does not round-trip even though nothing about it is refused. That bug, not a missing feature
/// alone, is what several tests below actually pin.</para>
/// </summary>
[TestFixture]
public class FormStripDocumentTests
{
    /// <summary>
    /// Spec §3's document, verbatim. <c>internal</c> — reused by <c>FormStripEmissionTests</c>
    /// (Task 15) rather than kept as a third copy of the same fixture.
    /// </summary>
    internal const string WithStrips = """
        <Form Name="MainForm" Version="1" Width="640" Height="480" Text="Main">
          <Controls>
            <MenuStrip Id="menuStrip1" Dock="Top">
              <ToolStripMenuItem Id="mnuFile" Text="&amp;File">
                <ToolStripMenuItem Id="mnuOpen" Text="&amp;Open...">
                  <Bind Event="Click" Handler="mnuOpen_Click"/>
                </ToolStripMenuItem>
                <ToolStripSeparator Id="sep1"/>
                <ToolStripMenuItem Id="mnuExit" Text="E&amp;xit"/>
              </ToolStripMenuItem>
            </MenuStrip>
            <ToolStrip Id="toolStrip1" Dock="Top" GripStyle="Hidden">
              <ToolStripButton Id="tsbOpen" Text="Open"/>
            </ToolStrip>
            <Button Id="btnGo" Text="Go" X="16" Y="80" Width="75" Height="23" TabIndex="0"/>
            <StatusStrip Id="statusStrip1" Dock="Bottom">
              <ToolStripStatusLabel Id="lblStatus" Text="Ready" Spring="true"/>
            </StatusStrip>
          </Controls>
          <Components/>
          <Resources/>
        </Form>
        """;

    private static FormFile Read(string xml, string name = "F.blform") =>
        FormDocumentReader.Read(Path.Combine("C:", "forms", name), xml);

    private static FormDocument Model(string xml, string name = "F.blform")
    {
        var file = Read(xml, name);
        Assert.That(file.IsRefused, Is.False,
            "fixture refused: " + string.Join("; ", file.Diagnostics.Select(d => d.Format())));
        return file.Model;
    }

    // ==================================================================
    // The Place branch
    // ==================================================================

    [Test]
    public void Read_AStrip_IsGeometryLess_WithDockAsAProperty()
    {
        var model = Model(WithStrips, "MainForm.blform");
        var menuStrip = model.Controls.Single(c => c.Id == "menuStrip1");

        Assert.Multiple(() =>
        {
            // ⚠ NOT a tautology: MenuStrip carries Dock="Top", and ReadGeometry treats Dock as one
            // of the six attributes that make it build a PixelGeometry — so today this is a REAL
            // non-null PixelGeometry{X=0,Y=0,Width=0,Height=0,Dock="Top"}, not an absent-defaults null.
            Assert.That(menuStrip.Geometry, Is.Null, "a strip docks to the form; it does not sit at pixels");

            // ⚠ NOT a tautology either: today Dock is in FormControlCatalog.StructuralAttributes, so
            // it is skipped out of the property loop entirely and never reaches Properties at all —
            // this indexer throws KeyNotFoundException against the unfixed reader.
            Assert.That(menuStrip.Properties["Dock"], Is.EqualTo("Top"), "Dock is a PROPERTY on a strip, not geometry");

            Assert.That(menuStrip.TabIndex, Is.Zero, "a strip has no tab order");
            Assert.That(menuStrip.Children.Select(c => c.Id), Is.EqualTo(new[] { "mnuFile" }),
                "document order");

            var mnuFile = menuStrip.Children.Single();
            Assert.That(mnuFile.Children.Select(c => c.Id), Is.EqualTo(new[] { "mnuOpen", "sep1", "mnuExit" }),
                "document order, sep1 between mnuOpen and mnuExit");
        });
    }

    /// <summary>
    /// ⚠⚠ Added beyond Task 13 Step 1's named list, to close a tautology the plan's own wording
    /// warns about. <see cref="Read_AStrip_IsGeometryLess_WithDockAsAProperty"/>'s <c>TabIndex ==
    /// 0</c> assertion holds against the UNCHANGED reader too, because the fixture's MenuStrip
    /// carries no TabIndex attribute at all and absence already reads as 0 — it pins nothing about
    /// the new Place branch. Here the strip carries every one of the four geometry attributes PLUS
    /// TabIndex, all with non-zero values, so a reader that still treated it as Positioned would
    /// read a non-null Geometry and TabIndex 9 — both wrong. Spec §3: "on read they fall through to
    /// unknown attributes, never to geometry."
    /// </summary>
    [Test]
    public void Read_AStripWithStrayGeometryAndTabIndex_KeepsThemAsUnknownAttributes_NeverAsGeometry()
    {
        var model = Model("""
            <Form Name="F" Version="1">
              <Controls>
                <MenuStrip Id="menuStrip1" Dock="Top" X="5" Y="6" Width="7" Height="8" TabIndex="9"/>
              </Controls>
              <Components/>
              <Resources/>
            </Form>
            """);
        var menuStrip = model.Controls.Single();

        Assert.Multiple(() =>
        {
            Assert.That(menuStrip.Geometry, Is.Null);
            Assert.That(menuStrip.TabIndex, Is.Zero);
            Assert.That(menuStrip.Properties["Dock"], Is.EqualTo("Top"));
            Assert.That(menuStrip.UnknownAttributes["X"], Is.EqualTo("5"));
            Assert.That(menuStrip.UnknownAttributes["Y"], Is.EqualTo("6"));
            Assert.That(menuStrip.UnknownAttributes["Width"], Is.EqualTo("7"));
            Assert.That(menuStrip.UnknownAttributes["Height"], Is.EqualTo("8"));
            Assert.That(menuStrip.UnknownAttributes["TabIndex"], Is.EqualTo("9"));
        });
    }

    [Test]
    public void Read_AnItem_HasNoGeometryAndNoTabIndex_AndKeepsStrayStructuralAttributes()
    {
        var model = Model("""
            <Form Name="F" Version="1">
              <Controls>
                <MenuStrip Id="menuStrip1" Dock="Top">
                  <ToolStripMenuItem Id="x" Text="T" X="5" TabIndex="3"/>
                </MenuStrip>
              </Controls>
              <Components/>
              <Resources/>
            </Form>
            """);
        var item = model.Controls.Single().Children.Single();

        Assert.Multiple(() =>
        {
            Assert.That(item.Geometry, Is.Null);
            Assert.That(item.TabIndex, Is.Zero);
            Assert.That(item.UnknownAttributes["X"], Is.EqualTo("5"));
            Assert.That(item.UnknownAttributes["TabIndex"], Is.EqualTo("3"));
        });
    }

    // ==================================================================
    // BL8030 — refused three ways (spec §3)
    // ==================================================================

    [Test]
    public void Read_RefusesAnItemOutsideAHost()
    {
        var file = Read("""
            <Form Name="F" Version="1">
              <Controls>
                <ToolStripMenuItem Id="mnuOpen" Text="Open"/>
              </Controls>
              <Components/>
              <Resources/>
            </Form>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(file.IsRefused, Is.True);
            Assert.That(file.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(file.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.StripMisplaced));
            Assert.That(file.Diagnostics.Single().Message, Does.Contain("'mnuOpen'"));
        });
    }

    [Test]
    public void Read_RefusesAControlInsideAHost()
    {
        var file = Read("""
            <Form Name="F" Version="1">
              <Controls>
                <MenuStrip Id="menuStrip1" Dock="Top">
                  <Button Id="btnGo" X="1" Y="1" Width="1" Height="1" TabIndex="0"/>
                </MenuStrip>
              </Controls>
              <Components/>
              <Resources/>
            </Form>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(file.IsRefused, Is.True);
            Assert.That(file.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(file.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.StripMisplaced));
            Assert.That(file.Diagnostics.Single().Message, Does.Contain("'btnGo'"));
        });
    }

    [Test]
    public void Read_RefusesAnItemKindTheHostDoesNotList()
    {
        // MenuStrip's Items rule lists ToolStripMenuItem/ToolStripSeparator only — never ToolStripButton.
        var file = Read("""
            <Form Name="F" Version="1">
              <Controls>
                <MenuStrip Id="menuStrip1" Dock="Top">
                  <ToolStripButton Id="tsbOpen" Text="Open"/>
                </MenuStrip>
              </Controls>
              <Components/>
              <Resources/>
            </Form>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(file.IsRefused, Is.True);
            Assert.That(file.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(file.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.StripMisplaced));
            Assert.That(file.Diagnostics.Single().Message, Does.Contain("'tsbOpen'"));
        });
    }

    [Test]
    public void Read_RefusesAStripBelowTheTopLevel()
    {
        var file = Read("""
            <Form Name="F" Version="1">
              <Controls>
                <Panel Id="pnl" X="0" Y="0" Width="100" Height="100" TabIndex="0">
                  <MenuStrip Id="menuStrip1" Dock="Top"/>
                </Panel>
              </Controls>
              <Components/>
              <Resources/>
            </Form>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(file.IsRefused, Is.True);
            Assert.That(file.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(file.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.StripMisplaced));
            Assert.That(file.Diagnostics.Single().Message, Does.Contain("'menuStrip1'"));
        });
    }

    // ==================================================================
    // D9's algebra, restated over a document with a nested strip (spec §3)
    // ==================================================================

    [Test]
    public void Algebra_RoundTrip_IsByteIdentical()
    {
        var file = Read(WithStrips, "MainForm.blform");
        Assert.That(file.IsRefused, Is.False,
            string.Join("; ", file.Diagnostics.Select(d => d.Format())));

        // ⚠ NOT a tautology: against the unfixed reader, Write deletes Dock="Top" from all three
        // strips (FormDocumentWriter.ApplyControl's "a catalog property the model dropped" sweep,
        // because the unfixed reader never put Dock into Properties), so this fails today with a
        // concrete three-attribute diff, not merely "some assertion somewhere."
        Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(WithStrips));
    }

    [Test]
    public void Algebra_ANoOpSave_WritesNothing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-strip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "MainForm.blform");
            File.WriteAllText(path, WithStrips);
            var file = FormDocumentReader.Read(path, WithStrips);

            // Save short-circuits on a REFUSED document too, and that False would look identical.
            Assert.That(file.IsRefused, Is.False, string.Join("; ", file.Diagnostics.Select(d => d.Format())));
            Assert.That(FormDocumentWriter.Save(file), Is.False);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Test]
    public void Algebra_ReadApply_EqualsApplyRead_ForAnItemEdit()
    {
        // Edit then write, versus write then re-read then edit — the same document.
        var a = Read(WithStrips, "MainForm.blform");
        a.Model.FindById("mnuOpen")!.Properties["Text"] = "&Open File...";

        var b = Read(FormDocumentWriter.Write(Read(WithStrips, "MainForm.blform")), "MainForm.blform");
        b.Model.FindById("mnuOpen")!.Properties["Text"] = "&Open File...";

        var writtenA = FormDocumentWriter.Write(a);
        var writtenB = FormDocumentWriter.Write(b);

        Assert.Multiple(() =>
        {
            Assert.That(writtenA, Is.EqualTo(writtenB));

            // ⚠⚠ The equality above is NOT sufficient on its own — it is a self-consistency check,
            // and against the unfixed reader/writer BOTH sides apply the identical Dock-deleting bug
            // (Write(Read(WithStrips)) already lost Dock before `b` ever reads it, and Write(a) loses
            // it again independently), so writtenA and writtenB agree with EACH OTHER while both
            // disagreeing with the document. A genuinely fixed pipeline must additionally preserve
            // Dock across an unrelated edit, which is what these two assertions pin.
            Assert.That(writtenA, Does.Contain("Dock=\"Top\""), "the MenuStrip's Dock must survive an unrelated edit");
            Assert.That(Model(writtenA, "MainForm.blform").FindById("menuStrip1")!.Properties["Dock"],
                Is.EqualTo("Top"));
        });
    }

    // ==================================================================
    // Task 14 — the writer and the clipboard mirror the reader's Place branch
    // ==================================================================

    [Test]
    public void Create_WritesNoZerosAndNoTabIndex_OnAStripOrItem()
    {
        var model = Model(WithStrips, "MainForm.blform");
        var doc = XDocument.Parse(FormDocumentWriter.Create(model));
        var menuStrip = doc.Root!.Element("Controls")!.Elements("MenuStrip").Single();

        Assert.Multiple(() =>
        {
            // ⚠⚠ NOT a tautology: ControlElement stamps TabIndex on every control
            // UNCONDITIONALLY (element.SetAttributeValue, never the incremental
            // SetIntAttributeIfChanged the Apply path uses) — so this fails today even though
            // the model's TabIndex is already 0 (the reader forces it), because Create writes
            // the attribute regardless of its value. Today's <MenuStrip> carries Id, TabIndex
            // AND Dock; this asserts it carries Id and Dock ONLY.
            Assert.That(menuStrip.Attributes().Select(a => a.Name.LocalName),
                Is.EquivalentTo(new[] { "Id", "Dock" }),
                "a strip has no tab order and no geometry — only Id and its Dock property");

            // Generalised over every strip/item element Create produced, not just the one
            // MenuStrip above — a ToolStrip, a StatusStrip and every item under them must be
            // equally TabIndex-less.
            foreach (var element in doc.Descendants())
            {
                var def = FormControlCatalog.Find(element.Name.LocalName);
                if (def != null && def.Place != FormPlace.Positioned)
                {
                    Assert.That(element.Attribute("TabIndex"), Is.Null,
                        $"'{(string?)element.Attribute("Id")}' ({element.Name.LocalName}, {def.Place}) " +
                        "must not carry a TabIndex");
                }
            }
        });
    }

    [Test]
    public void Apply_NeverWritesTabIndexOnAnItem()
    {
        var file = Read(WithStrips, "MainForm.blform");
        Assert.That(file.IsRefused, Is.False, string.Join("; ", file.Diagnostics.Select(d => d.Format())));

        // ⚠⚠ TAUTOLOGY GUARD, per the implementer's own warning: a bare renumber-then-write over
        // this fixture would be GREEN against the UNCHANGED writer too, because
        // SetIntAttributeIfChanged is a silent no-op when the attribute is absent and the value
        // equals the default 0 (FormDocumentWriter.cs:655-667) — and mnuOpen starts at TabIndex 0
        // with no TabIndex attribute in the fixture. What makes this genuinely red is
        // RenumberTabIndexes ITSELF: unfixed, it walks AllControls() with no Place filter, so
        // mnuOpen (document order index 2, counting menuStrip1=0, mnuFile=1) is assigned TabIndex
        // 2 — a value that no longer equals the default, so SetIntAttributeIfChanged genuinely
        // writes TabIndex="2" onto the element.
        file.Model.RenumberTabIndexes();
        var written = FormDocumentWriter.Write(file);

        var doc = XDocument.Parse(written);
        var mnuOpen = doc.Descendants("ToolStripMenuItem")
            .Single(e => (string?)e.Attribute("Id") == "mnuOpen");

        Assert.That(mnuOpen.Attribute("TabIndex"), Is.Null,
            "an item never has a tab order, however the document was renumbered");
    }

    [Test]
    public void RenumberTabIndexes_SkipsStripsAndItems()
    {
        var model = Model(WithStrips, "MainForm.blform");

        model.RenumberTabIndexes();

        // ⚠⚠ NOT a tautology: btnGo is the ONLY Place.Positioned control in the whole fixture.
        // An unfiltered AllControls() walk (today) assigns it the DOCUMENT-ORDER index 7
        // (menuStrip1=0, mnuFile=1, mnuOpen=2, sep1=3, mnuExit=4, toolStrip1=5, tsbOpen=6,
        // btnGo=7…) — a Place-aware renumber that skips strips and items must instead give it
        // index 0, because it is the first (and only) Positioned control.
        var btnGo = model.Controls.Single(c => c.Id == "btnGo");
        var menuStrip = model.Controls.Single(c => c.Id == "menuStrip1");
        var mnuOpen = menuStrip.Children.Single(c => c.Id == "mnuFile").Children.Single(c => c.Id == "mnuOpen");

        Assert.Multiple(() =>
        {
            Assert.That(btnGo.TabIndex, Is.Zero,
                "btnGo is the only Positioned control here — it must get index 0, not 7");
            Assert.That(menuStrip.TabIndex, Is.Zero, "a strip is never renumbered");
            Assert.That(mnuOpen.TabIndex, Is.Zero, "an item is never renumbered");
        });
    }

    [Test]
    public void Clipboard_ASerializedStrip_ComesBackGeometryLess_WithDockKept_AndNoTabIndex()
    {
        var model = Model(WithStrips, "MainForm.blform");
        var menuStrip = model.Controls.Single(c => c.Id == "menuStrip1");

        // A stale/non-zero TabIndex, exactly as a buggy renumber (see above) could leave one —
        // proves the clipboard FORCES it back to 0 rather than merely round-tripping a value
        // that already happened to be 0.
        menuStrip.TabIndex = 3;

        var xml = FormClipboard.SerializeSubtree(FormTarget.WinForms, new[] { menuStrip });
        var pasted = FormClipboard.DeserializeSubtree(xml, FormTarget.WinForms, _ => false);

        Assert.That(pasted, Has.Count.EqualTo(1));
        var copy = pasted[0];

        Assert.Multiple(() =>
        {
            // ⚠⚠ NOT a tautology, three ways. FormClipboard.ToElement/FromElement both decide
            // "is this a component?" from Definition.IsComponent, which is true ONLY for
            // Place.Tray — so a Docked strip takes the isComponent==false path on BOTH sides:
            //  - ToElement writes TabIndex="3" unconditionally (no place check), so the fragment
            //    itself carries the stale value;
            //  - FromElement's own ReadGeometry treats the still-present "Dock" attribute as one
            //    of its six PIXEL triggers and reconstructs a non-null PixelGeometry{Dock="Top"}
            //    instead of leaving Geometry null;
            //  - and because "Dock" is IsStructural(WinForms), FromElement's property loop skips
            //    it entirely, so it never lands in Properties either — copy.Properties has no
            //    "Dock" key at all.
            // All three assertions fail against the unfixed clipboard.
            Assert.That(copy.Geometry, Is.Null, "a strip docks to the form; it does not sit at pixels");
            Assert.That(copy.Properties.GetValueOrDefault("Dock"), Is.EqualTo("Top"),
                "Dock is a PROPERTY on a strip, not geometry");
            Assert.That(copy.TabIndex, Is.Zero, "a strip has no tab order, however it arrived with one");
        });
    }

    [Test]
    public void Clipboard_AnItemRoot_KeepsItsChildren()
    {
        var model = Model(WithStrips, "MainForm.blform");
        var mnuFile = model.Controls.Single(c => c.Id == "menuStrip1").Children.Single(c => c.Id == "mnuFile");

        // Same stale-value proof as the strip test above, applied to an ITEM used as the
        // clipboard root (rather than nested under a strip) — a copy/paste of just a submenu.
        mnuFile.TabIndex = 7;

        var xml = FormClipboard.SerializeSubtree(FormTarget.WinForms, new[] { mnuFile });
        var pasted = FormClipboard.DeserializeSubtree(xml, FormTarget.WinForms, _ => false);

        Assert.That(pasted, Has.Count.EqualTo(1));
        var copy = pasted[0];

        Assert.Multiple(() =>
        {
            Assert.That(copy.Id, Is.EqualTo("mnuFile"));

            // This part alone is NOT a red/green distinguisher — FromElement already recurses
            // into an item's children regardless of Place (isComponent is Tray-only), so
            // children survive even against the unfixed clipboard. Asserted anyway because the
            // test's job is to pin that an item root — itself a host — keeps working once the
            // TabIndex/Geometry forcing below is added; a fix that broke recursion for a
            // Place.Item root would fail here even though nothing about TabIndex changed.
            Assert.That(copy.Children.Select(c => c.Id), Is.EqualTo(new[] { "mnuOpen", "sep1", "mnuExit" }),
                "an item root copies its own children in document order — an item can be a host too");

            // ⚠⚠ Genuinely red today, same mechanism as the strip test: ToElement writes
            // TabIndex="7" unconditionally and FromElement reads it straight back.
            Assert.That(copy.TabIndex, Is.Zero,
                "the root item itself never has a tab order, however it arrived at this value");
            Assert.That(copy.Geometry, Is.Null);
        });
    }

    // ==================================================================
    // Task 13 coverage — an uncovered arm found while building it (no test existed)
    // ==================================================================

    /// <summary>
    /// ⚠ This is a COVERAGE test, not a red one: the reader arm it exercises already exists
    /// (added by the Task 13 implementer alongside the plan's two documented BL8030 arms) and is
    /// expected to be GREEN. It pins an item under a NON-HOST, NON-NULL parent — e.g.
    /// <c>&lt;Panel&gt;&lt;ToolStripMenuItem/&gt;&lt;/Panel&gt;</c> — where <c>parentDefinition</c>
    /// is non-null but its <c>Items</c> rule is null, so the plan's literal wrong-host wording
    /// (which reads <c>rule.Kinds</c>) would have thrown an NRE before ever building a message.
    /// An arm added to prevent a crash, with no test, is exactly the failure pattern this repo
    /// keeps getting burned by — see FormDocumentReader.cs:380-393.
    /// </summary>
    [Test]
    public void Read_RefusesAnItemUnderANonHostContainer()
    {
        var file = Read("""
            <Form Name="F" Version="1">
              <Controls>
                <Panel Id="pnl" X="0" Y="0" Width="100" Height="100" TabIndex="0">
                  <ToolStripMenuItem Id="x" Text="T"/>
                </Panel>
              </Controls>
              <Components/>
              <Resources/>
            </Form>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(file.IsRefused, Is.True);
            Assert.That(file.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(file.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.StripMisplaced));
            Assert.That(file.Diagnostics.Single().Message, Does.Contain("'x'"),
                "must name the misplaced item");
            Assert.That(file.Diagnostics.Single().Message, Does.Contain("'pnl'"),
                "must name the parent it wrongly sits under");
        });
    }
}
