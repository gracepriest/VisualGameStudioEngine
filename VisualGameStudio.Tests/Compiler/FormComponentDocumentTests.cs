using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 25 — a component is a <see cref="FormControl"/> with no place: the model, the reader and
/// the writer.
///
/// <para>⛔ Until this fixture, no test in the suite carried a NON-EMPTY <c>&lt;Components&gt;</c>:
/// every fixture had <c>&lt;Components/&gt;</c>, so a writer that dropped, duplicated or mis-ordered
/// component children would have passed every algebra test. The D9 laws are re-stated here over a
/// document that actually has components, because that is the document the tray produces.</para>
/// </summary>
[TestFixture]
public class FormComponentDocumentTests
{
    /// <summary>A form with two components — one wired, one carrying an unknown attribute (D9).</summary>
    private const string WithComponents = """
        <Form Name="F" Version="1" Width="400" Height="300" Text="F">
          <Controls>
            <Button Id="btn" Text="Go" X="8" Y="8" Width="75" Height="23" TabIndex="0"/>
          </Controls>
          <Components>
            <Timer Id="tmr" Interval="500" Enabled="true" Note="keep me">
              <Bind Event="Tick" Handler="tmr_Tick"/>
            </Timer>
            <ToolTip Id="tip" InitialDelay="300"/>
          </Components>
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
    // The model
    // ==================================================================

    [Test]
    public void FindById_AndListContaining_SeeComponents_BecauseTheyShareTheFieldNamespace()
    {
        var doc = new FormDocument { Target = FormTarget.WinForms, Name = "F" };
        var tmr = new FormControl { Kind = "Timer", Id = "tmr" };
        doc.Components.Add(tmr);

        Assert.Multiple(() =>
        {
            Assert.That(doc.FindById("tmr"), Is.SameAs(tmr), "a component id is a field name like any control's");
            Assert.That(doc.ListContaining(tmr), Is.SameAs(doc.Components), "Delete and Cut remove through this");
            Assert.That(doc.AllControls(), Is.Empty, "the visual tree is unchanged");
            Assert.That(doc.AllComponents(), Is.EqualTo(new[] { tmr }));
        });
    }

    // ==================================================================
    // The reader
    // ==================================================================

    [Test]
    public void Read_RecoversComponents_AsControlsWithNoPlace()
    {
        var model = Model(WithComponents);
        var tmr = model.Components.Single(c => c.Id == "tmr");

        Assert.Multiple(() =>
        {
            Assert.That(model.Components.Select(c => c.Kind), Is.EqualTo(new[] { "Timer", "ToolTip" }));
            Assert.That(tmr.Geometry, Is.Null, "a component has no position");
            Assert.That(tmr.TabIndex, Is.Zero, "and no tab order");
            Assert.That(tmr.Properties["Interval"], Is.EqualTo("500"));
            Assert.That(tmr.Binds.Single().Handler, Is.EqualTo("tmr_Tick"));
            Assert.That(tmr.UnknownAttributes["Note"], Is.EqualTo("keep me"), "D9: unknown content round-trips");
            Assert.That(model.Controls, Has.Count.EqualTo(1), "components are not controls");
            Assert.That(model.FindById("tip")!.Kind, Is.EqualTo("ToolTip"));
        });
    }

    [Test]
    public void Read_TreatsLayoutAttributesOnAComponent_AsUnknown_NeverAsGeometry()
    {
        // A stray X= or TabIndex= on a component is text the designer does not model. Reading it
        // as geometry would give a Timer a position; dropping it would break D9.
        var model = Model("""
            <Form Name="F" Version="1">
              <Controls/>
              <Components><Timer Id="tmr" X="10" TabIndex="3"/></Components>
            </Form>
            """);
        var tmr = model.Components.Single();

        Assert.Multiple(() =>
        {
            Assert.That(tmr.Geometry, Is.Null);
            Assert.That(tmr.TabIndex, Is.Zero);
            Assert.That(tmr.UnknownAttributes.Keys, Is.EquivalentTo(new[] { "X", "TabIndex" }));
        });
    }

    [Test]
    public void Read_RefusesAComponentKindUnderControls_AndAControlKindUnderComponents()
    {
        // Both would generate code csc rejects: Me.Controls.Add(tmr) for a Timer, and a Button that
        // is constructed and never added. A document that says two different things about where a
        // thing lives is refused rather than half-read.
        var timerAsControl = Read("""
            <Form Name="F" Version="1">
              <Controls>
                <Timer Id="tmr" X="8" Y="8" Width="1" Height="1" TabIndex="0"/>
              </Controls>
            </Form>
            """);
        var buttonAsComponent = Read("""
            <Form Name="F" Version="1">
              <Controls/>
              <Components><Button Id="btn"/></Components>
            </Form>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(timerAsControl.IsRefused, Is.True);
            Assert.That(timerAsControl.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.ComponentMisplaced));
            Assert.That(timerAsControl.Diagnostics.Single().Line, Is.EqualTo(3), "the element's own line");
            Assert.That(timerAsControl.Diagnostics.Single().Message, Does.Contain("'tmr'").And.Contain("<Components>"));

            Assert.That(buttonAsComponent.IsRefused, Is.True);
            Assert.That(buttonAsComponent.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.ComponentMisplaced));
            Assert.That(buttonAsComponent.Diagnostics.Single().Message, Does.Contain("'btn'").And.Contain("<Controls>"));
        });
    }

    [Test]
    public void Read_RefusesAComponentSharingAnIdWithAControl()
    {
        var file = Read("""
            <Form Name="F" Version="1">
              <Controls><Button Id="x" X="8" Y="8" Width="1" Height="1" TabIndex="0"/></Controls>
              <Components><Timer Id="x"/></Components>
            </Form>
            """);

        Assert.That(file.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.DuplicateControlId),
            "one class, one field namespace");
    }

    [Test]
    public void Read_AcceptsComponents_OnAWebDocument()
    {
        var model = Model("""
            <WebForm Name="F" Version="1">
              <Layout Kind="Grid"/>
              <Controls/>
              <Components><Timer Id="tmr" Interval="50"><Bind Event="tick" Handler="tmr_Tick"/></Timer></Components>
            </WebForm>
            """, "F.blwebform");

        Assert.That(model.Components.Single().Binds.Single().Event, Is.EqualTo("tick"));
    }

    // ==================================================================
    // The writer — D9's algebra over a document that HAS components
    // ==================================================================

    [Test]
    public void Algebra_ARoundTripWithComponents_IsByteIdentical()
    {
        var file = Read(WithComponents);
        Assert.That(file.IsRefused, Is.False);

        Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(WithComponents));
    }

    [Test]
    public void Algebra_ANoOpPatchWithComponents_WritesNothing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-tray-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "F.blform");
            File.WriteAllText(path, WithComponents);
            var file = FormDocumentReader.Read(path, WithComponents);

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
    public void Algebra_ReadApply_EqualsApplyRead_ForAComponentEdit()
    {
        // Edit then write, versus write then re-read then edit — the same document.
        var a = Read(WithComponents);
        a.Model.Components[0].Properties["Interval"] = "1";

        var b = Read(FormDocumentWriter.Write(Read(WithComponents)));
        b.Model.Components[0].Properties["Interval"] = "1";

        Assert.That(FormDocumentWriter.Write(a), Is.EqualTo(FormDocumentWriter.Write(b)));
    }

    [Test]
    public void Write_PersistsAComponentEdit_AnAddition_ARemoval_AndAReorder()
    {
        var file = Read(WithComponents);
        var tmr = file.Model.Components[0];
        tmr.Properties["Interval"] = "250";
        file.Model.Components.Add(new FormControl { Kind = "ErrorProvider", Id = "err" });
        file.Model.Components.Remove(file.Model.Components.Single(c => c.Id == "tip"));
        file.Model.Components.Reverse();   // err, tmr

        var text = FormDocumentWriter.Write(file);
        var back = Model(text);

        Assert.Multiple(() =>
        {
            Assert.That(back.Components.Select(c => c.Id), Is.EqualTo(new[] { "err", "tmr" }), "order is the model's");
            Assert.That(back.Components[1].Properties["Interval"], Is.EqualTo("250"));
            Assert.That(back.Components[1].UnknownAttributes["Note"], Is.EqualTo("keep me"));
            Assert.That(back.Components[1].Binds.Single().Handler, Is.EqualTo("tmr_Tick"));
            Assert.That(text, Does.Not.Match("<ErrorProvider[^>]*TabIndex"), "no TabIndex is invented for a component");
            Assert.That(text, Does.Not.Match("<ErrorProvider[^>]*\\bX="), "and no geometry");
            Assert.That(text, Does.Not.Contain("tip"), "the removed one is gone");
        });
    }

    [Test]
    public void Write_LeavesAComponentsUnknownTabIndexAlone_OnAnUnrelatedEdit()
    {
        // ⛔ The APPLY route, not Create: ApplyControl's TabIndex write would rewrite a component's
        // TabIndex="5" — an unknown attribute on a component — to "0" on the first real edit.
        var file = Read("""
            <Form Name="F" Version="1">
              <Controls/>
              <Components><Timer Id="tmr" TabIndex="5" Interval="9"/></Components>
            </Form>
            """);
        file.Model.Components[0].Properties["Interval"] = "10";

        var text = FormDocumentWriter.Write(file);

        Assert.That(text, Does.Contain("TabIndex=\"5\"").And.Contain("Interval=\"10\""));
    }

    [Test]
    public void Write_AddsTheComponentsElement_BeforeResources_WhenTheDocumentHadNone()
    {
        var file = Read("""
            <Form Name="F" Version="1">
              <Controls/>
              <Resources/>
            </Form>
            """);
        file.Model.Components.Add(new FormControl { Kind = "Timer", Id = "tmr" });

        var text = FormDocumentWriter.Write(file);

        Assert.Multiple(() =>
        {
            Assert.That(text.IndexOf("<Components>", StringComparison.Ordinal),
                Is.GreaterThan(0).And.LessThan(text.IndexOf("<Resources", StringComparison.Ordinal)));
            Assert.That(Model(text).Components.Single().Id, Is.EqualTo("tmr"));
        });
    }

    [Test]
    public void Write_LeavesADocumentWithNoComponentsElement_Alone_WhenTheModelHasNone()
    {
        // A document that never had the element keeps not having it — D9's no-op law.
        const string bare = """
            <Form Name="F" Version="1">
              <Controls/>
            </Form>
            """;
        var file = Read(bare);
        Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(bare));
    }

    [Test]
    public void Create_WritesTheModelsComponents()
    {
        var doc = new FormDocument { Target = FormTarget.Web, Name = "F", Layout = new FormLayout() };
        var tmr = new FormControl { Kind = "Timer", Id = "tmr" };
        tmr.Properties["Interval"] = "50";
        tmr.Binds.Add(new FormBind { Event = "tick", Handler = "tmr_Tick" });
        doc.Components.Add(tmr);

        var text = FormDocumentWriter.Create(doc);
        var back = Model(text, "F.blwebform");

        Assert.Multiple(() =>
        {
            Assert.That(back.Components.Single().Properties["Interval"], Is.EqualTo("50"));
            Assert.That(back.Components.Single().Binds.Single().Handler, Is.EqualTo("tmr_Tick"));
            Assert.That(text, Does.Not.Match("<Timer[^>]*TabIndex"), "Create invents no TabIndex either");
        });
    }

    [Test]
    public void Create_StillWritesAnEmptyComponentsElement_ForAFormWithNone()
    {
        // The shape of a new document is unchanged: the scaffolder's fixtures and the acceptance
        // walkthroughs all carry <Components />.
        var text = FormDocumentWriter.Create(new FormDocument { Target = FormTarget.WinForms, Name = "F" });
        Assert.That(text, Does.Contain("<Components />"));
    }
}
