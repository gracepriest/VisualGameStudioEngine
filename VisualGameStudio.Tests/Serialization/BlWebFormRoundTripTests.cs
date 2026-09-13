using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Serialization;

/// <summary>
/// Task 9: the <c>.blwebform</c> reader/writer, D9's tiers, and the algebra.
///
/// <para>⛔ The algebra tests are the point of this fixture, not decoration. D1 puts designer-written
/// regions inside files the user owns, and D9 is the mechanism that makes that safe — so "a no-op
/// patch writes nothing", "a round trip is byte-identical" and <c>Read∘Apply == Apply∘Read</c> are
/// the properties the whole design rests on. If they do not hold, the designer corrupts user files
/// slowly instead of obviously.</para>
/// </summary>
[TestFixture]
public class BlWebFormRoundTripTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-webform-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    /// <summary>The spec's worked example, verbatim, plus a comment and an unknown element.</summary>
    private const string LoginForm = """
        <WebForm Name="LoginForm" Version="1">
          <Layout Kind="Grid" Cols="120px,1fr" Rows="auto,auto" Gap="8px"/>
          <!-- the designer must not eat this comment -->
          <Controls>
            <Label   Id="lblUser"  Text="User"     Col="0" Row="0" TabIndex="0"/>
            <TextBox Id="txtUser"  Col="1" Row="0" TabIndex="1">
              <Bind Event="input" Handler="txtUser_Input"/>
            </TextBox>
            <Button  Id="btnLogin" Text="Sign in"  Col="1" Row="1" TabIndex="2">
              <Bind Event="click" Handler="btnLogin_Click"/>
            </Button>
          </Controls>
          <Literal><![CDATA[<p class="hint">Use your work account.</p>]]></Literal>
          <Components/>
          <Resources/>
          <FutureSection Something="42"/>
        </WebForm>
        """;

    private BlWebForm Read(string xml, string name = "LoginForm.blwebform")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, xml);
        return BlWebFormReader.Read(path, xml);
    }

    // ==================================================================
    // Reading
    // ==================================================================

    [Test]
    public void Read_RecoversTheSpecsWorkedExample()
    {
        var form = Read(LoginForm);

        Assert.Multiple(() =>
        {
            Assert.That(form.IsRefused, Is.False, string.Join("; ", form.Diagnostics.Select(d => d.Format())));
            Assert.That(form.Model.Name, Is.EqualTo("LoginForm"));
            Assert.That(form.Model.Version, Is.EqualTo(1));
            Assert.That(form.Model.Controls.Select(c => c.Id),
                Is.EqualTo(new[] { "lblUser", "txtUser", "btnLogin" }), "document order is z-order");
            Assert.That(form.Model.Layout!.Kind, Is.EqualTo(FormLayoutKind.Grid));
            Assert.That(form.Model.Layout.Cols, Is.EqualTo("120px,1fr"),
                "a CSS track list is the author's own text and must not be normalised");
            Assert.That(form.Model.Literal, Is.EqualTo("""<p class="hint">Use your work account.</p>"""));
        });

        var button = form.Model.FindById("btnLogin")!;
        Assert.Multiple(() =>
        {
            Assert.That(button.Kind, Is.EqualTo("Button"));
            Assert.That(button.TabIndex, Is.EqualTo(2));
            Assert.That(button.Properties["Text"], Is.EqualTo("Sign in"));
            Assert.That(((GridGeometry)button.Geometry!).Col, Is.EqualTo(1));
            Assert.That(((GridGeometry)button.Geometry!).Row, Is.EqualTo(1));
            Assert.That(button.Binds.Single().Event, Is.EqualTo("click"));
            Assert.That(button.Binds.Single().Handler, Is.EqualTo("btnLogin_Click"));
        });
    }

    [Test]
    public void Read_CarriesLineAndColumn_OnEveryDiagnostic()
    {
        // LoadOptions.SetLineInfo is what makes this possible; without it every finding points at
        // the top of the document and nobody can act on it.
        var form = Read("""
            <WebForm Name="F" Version="1">
              <Controls>
                <Button Id="b" TabIndex="0">
                  <Bind Event="click" Handler="h" Property="Text" Source="vm" Path="Name"/>
                </Button>
              </Controls>
            </WebForm>
            """);

        var refusal = form.Diagnostics.Single(d => d.Code == DesignCodes.ReservedBindingPopulated);
        Assert.Multiple(() =>
        {
            Assert.That(refusal.Line, Is.EqualTo(4), "the line of the <Bind> itself");
            Assert.That(refusal.Column, Is.GreaterThan(0));
            Assert.That(refusal.IsWarning, Is.False, "a populated reserved binding is a document refusal");
        });
    }

    [Test]
    public void Read_RefusesAPopulatedReservedBinding_RatherThanIgnoringIt()
    {
        // Ignoring it would leave the user believing a data binding exists, and nothing in the
        // running page would ever tell them otherwise.
        var form = Read("""
            <WebForm Name="F" Version="1">
              <Controls><Button Id="b" TabIndex="0"><Bind Event="click" Handler="h" Source="vm"/></Button></Controls>
            </WebForm>
            """);

        Assert.That(form.IsRefused, Is.True);
        Assert.That(form.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.ReservedBindingPopulated));
    }

    [Test]
    public void Read_RefusesAReservedResourceReference()
    {
        var form = Read("""
            <WebForm Name="F" Version="1">
              <Controls><Button Id="b" TabIndex="0" Text="{res:SignIn}"/></Controls>
            </WebForm>
            """);

        Assert.That(form.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.ReservedResourceReference));
        Assert.That(form.IsRefused, Is.True, "<Resources> is empty in v1, so the reference resolves to nothing");
    }

    [Test]
    public void Read_RefusesADocumentFromANewerVersion()
    {
        var form = Read("""<WebForm Name="F" Version="99"><Controls/></WebForm>""");

        Assert.That(form.IsRefused, Is.True,
            "reading the parts we recognise and writing it back would drop whatever v99 added — " +
            "unknown-element round-tripping only covers additions we can still see");
        Assert.That(form.Diagnostics.Single().Message, Does.Contain("version 99"));
    }

    [Test]
    public void Read_ReportsMalformedXml_WithItsPosition_RatherThanThrowing()
    {
        BlWebForm form = null!;
        Assert.DoesNotThrow(() => form = Read("<WebForm Name=\"F\"><Controls>"));

        Assert.Multiple(() =>
        {
            Assert.That(form.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.MalformedDocument));
            Assert.That(form.Diagnostics.Single().Line, Is.GreaterThan(0));
        });
    }

    // ==================================================================
    // D9 tiers
    // ==================================================================

    [Test]
    public void Tier_IsCanon_WhenTheCatalogKnowsTheAttributeAndTheValueParses()
    {
        var form = Read("""
            <WebForm Name="F" Version="1">
              <Controls><CheckBox Id="chk" TabIndex="0" Checked="true"/></Controls>
            </WebForm>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(form.TierOf("chk", "Checked"), Is.EqualTo(PropertyTier.Canon));
            Assert.That(form.Degraded, Is.Empty);
            Assert.That(form.IsRefused, Is.False);
        });
    }

    [Test]
    public void Tier_IsDegraded_ForOneBadValue_AndTheRestOfTheControlStaysCanon()
    {
        // The unit is one property on one control. This is the entire reason D9 tiers per property
        // rather than per control: one typo must not freeze the whole control.
        var form = Read("""
            <WebForm Name="F" Version="1">
              <Controls><CheckBox Id="chk" TabIndex="0" Checked="yes please" Text="Remember me"/></Controls>
            </WebForm>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(form.TierOf("chk", "Checked"), Is.EqualTo(PropertyTier.Degraded));
            Assert.That(form.TierOf("chk", "Text"), Is.EqualTo(PropertyTier.Canon),
                "one unparseable value freezes ONE row, not the control");
            Assert.That(form.IsRefused, Is.False, "Degraded is a property-level tier, never a document refusal");
            Assert.That(form.DegradedReason("chk", "Checked"), Does.Contain("Bool"));
        });
    }

    [Test]
    public void Tier_IsUnknown_ForAnAttributeTheCatalogDoesNotKnow()
    {
        var form = Read("""
            <WebForm Name="F" Version="1">
              <Controls><Button Id="b" TabIndex="0" FutureThing="42"/></Controls>
            </WebForm>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(form.TierOf("b", "FutureThing"), Is.EqualTo(PropertyTier.Unknown),
                "neither Degraded nor Refused — this is what makes the format forward-compatible");
            Assert.That(form.Degraded, Is.Empty);
            Assert.That(form.IsRefused, Is.False);
            Assert.That(form.Model.FindById("b")!.UnknownAttributes["FutureThing"], Is.EqualTo("42"));
        });
    }

    [Test]
    public void Degraded_RoundTripsItsValueUnchanged()
    {
        var xml = """
            <WebForm Name="F" Version="1">
              <Controls><CheckBox Id="chk" TabIndex="0" Checked="yes please"/></Controls>
            </WebForm>
            """;
        var form = Read(xml);

        Assert.That(BlWebFormWriter.Write(form), Is.EqualTo(xml),
            "a frozen value must be written back EXACTLY as the user wrote it — a writer that " +
            "normalised it would silently 'fix' something it does not understand");
    }

    // ==================================================================
    // The algebra
    // ==================================================================

    [Test]
    public void Algebra_RoundTripIsByteIdentical()
    {
        var form = Read(LoginForm);

        Assert.That(BlWebFormWriter.Write(form), Is.EqualTo(LoginForm),
            "reading and writing with no edit must reproduce the file exactly — comments, the " +
            "unknown <FutureSection>, attribute spelling and indentation included");
    }

    [Test]
    public void Algebra_ANoOpPatchWritesNothing()
    {
        var path = Path.Combine(_dir, "NoOp.blwebform");
        File.WriteAllText(path, LoginForm);
        var before = File.GetLastWriteTimeUtc(path);

        var form = BlWebFormReader.Read(path, LoginForm);
        var wrote = BlWebFormWriter.Save(form);

        Assert.Multiple(() =>
        {
            Assert.That(wrote, Is.False, "nothing changed, so nothing may be written");
            Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(before),
                "a save that touches the file on every canvas tick makes every Build/F5 look like an edit");
        });
    }

    [Test]
    public void Algebra_ReadAfterApply_EqualsApplyAfterRead()
    {
        // Read∘Apply == Apply∘Read. Both sides make the same edit; the results must agree, or the
        // reader and the writer disagree about what the document means and corruption is slow.
        var edited = Read(LoginForm);
        edited.Model.FindById("btnLogin")!.Properties["Text"] = "Log in";
        var applyThenRead = BlWebFormReader.Read(edited.FilePath, BlWebFormWriter.Write(edited));

        var readThenApply = Read(LoginForm);
        readThenApply.Model.FindById("btnLogin")!.Properties["Text"] = "Log in";

        Assert.Multiple(() =>
        {
            Assert.That(applyThenRead.Model.FindById("btnLogin")!.Properties["Text"], Is.EqualTo("Log in"));
            Assert.That(applyThenRead.Model.Controls.Select(c => c.Id),
                Is.EqualTo(readThenApply.Model.Controls.Select(c => c.Id)));
            Assert.That(BlWebFormWriter.Write(applyThenRead), Is.EqualTo(BlWebFormWriter.Write(readThenApply)),
                "writing either side must give the same document");
        });
    }

    [Test]
    public void Write_KeepsEverythingItDoesNotOwn_WhenSomethingDidChange()
    {
        var form = Read(LoginForm);
        form.Model.FindById("btnLogin")!.Properties["Text"] = "Log in";

        var after = BlWebFormWriter.Write(form);

        Assert.Multiple(() =>
        {
            Assert.That(after, Does.Contain("Log in"));
            Assert.That(after, Does.Not.Contain("Sign in"));
            Assert.That(after, Does.Contain("<!-- the designer must not eat this comment -->"),
                "a comment is the clearest thing a rebuild-from-model writer destroys");
            Assert.That(after, Does.Contain("FutureSection").And.Contain("""Something="42" """.TrimEnd()),
                "an element from a newer designer must survive an unrelated edit");
            Assert.That(after, Does.Contain("""Cols="120px,1fr" """.TrimEnd()));
            Assert.That(after, Does.Contain("Use your work account."));
            Assert.That(after, Does.Contain("txtUser_Input"), "an untouched control's binding survives");
        });
    }

    [Test]
    public void Write_IsStable_AfterTheFirstRealEdit()
    {
        // The normalisation boundary, pinned rather than discovered. XmlWriter rewrites empty
        // elements as `<x />` and normalises quoting, so the FIRST real edit to a hand-written
        // document reformats it in those ways. What must not happen is churn on every save after
        // that — so a second write of an already-written document is byte-identical.
        var form = Read(LoginForm);
        form.Model.FindById("btnLogin")!.Properties["Text"] = "Log in";
        var first = BlWebFormWriter.Write(form);

        var reloaded = BlWebFormReader.Read(form.FilePath, first);
        Assert.Multiple(() =>
        {
            Assert.That(BlWebFormWriter.Write(reloaded), Is.EqualTo(first),
                "a no-op write of an already-written document must return it unchanged");
            Assert.That(reloaded.Model.FindById("btnLogin")!.Properties["Text"], Is.EqualTo("Log in"));
            Assert.That(reloaded.Model.Controls.Select(c => c.Id),
                Is.EqualTo(new[] { "lblUser", "txtUser", "btnLogin" }),
                "normalisation changes bytes, never meaning");
        });
    }

    [Test]
    public void Write_RemovesAControlTheModelDropped_AndLeavesUnknownElementsAlone()
    {
        var form = Read(LoginForm);
        form.Model.Controls.RemoveAll(c => c.Id == "txtUser");

        var after = BlWebFormWriter.Write(form);

        Assert.Multiple(() =>
        {
            Assert.That(after, Does.Not.Contain("txtUser"));
            Assert.That(after, Does.Contain("lblUser").And.Contain("btnLogin"));
            Assert.That(after, Does.Contain("<FutureSection"));
        });
    }

    [Test]
    public void Write_AddsANewControl()
    {
        var form = Read(LoginForm);
        var added = new FormControl
        {
            Kind = "Label", Id = "lblError", TabIndex = 3,
            Geometry = new GridGeometry { Col = 1, Row = 2 }
        };
        added.Properties["Text"] = "Bad password";
        form.Model.Controls.Add(added);

        var after = BlWebFormWriter.Write(form);
        var reloaded = BlWebFormReader.Read(form.FilePath, after);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Model.Controls.Select(c => c.Id),
                Is.EqualTo(new[] { "lblUser", "txtUser", "btnLogin", "lblError" }));
            Assert.That(reloaded.Model.FindById("lblError")!.Properties["Text"], Is.EqualTo("Bad password"));
            Assert.That(reloaded.Model.FindById("lblError")!.TabIndex, Is.EqualTo(3),
                "TabIndex is written on every control in v1");
        });
    }

    // ==================================================================
    // Creating from scratch
    // ==================================================================

    [Test]
    public void Create_ProducesADocumentThatReadsBackIdentically()
    {
        var model = new FormDocument { Target = FormTarget.Web, Name = "Fresh" };
        model.Layout = new FormLayout { Kind = FormLayoutKind.Grid, Cols = "1fr", Rows = "auto", Gap = "4px" };
        var button = new FormControl { Kind = "Button", Id = "btnGo", Geometry = new GridGeometry() };
        button.Properties["Text"] = "Go";
        button.Binds.Add(new FormBind { Event = "click", Handler = "btnGo_Click" });
        model.Controls.Add(button);
        model.RenumberTabIndexes();

        var text = BlWebFormWriter.Create(model);
        var reloaded = BlWebFormReader.Read(Path.Combine(_dir, "Fresh.blwebform"), text);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.IsRefused, Is.False,
                string.Join("; ", reloaded.Diagnostics.Select(d => d.Format())));
            Assert.That(reloaded.Model.Name, Is.EqualTo("Fresh"));
            Assert.That(reloaded.Model.Layout!.Cols, Is.EqualTo("1fr"));
            Assert.That(reloaded.Model.Controls.Single().Id, Is.EqualTo("btnGo"));
            Assert.That(reloaded.Model.Controls.Single().Properties["Text"], Is.EqualTo("Go"));
            Assert.That(reloaded.Model.Controls.Single().Binds.Single().Handler, Is.EqualTo("btnGo_Click"));
            Assert.That(reloaded.Model.Controls.Single().TabIndex, Is.Zero);
        });
    }

    [Test]
    public void Create_WritesAttributesInADeterministicOrder()
    {
        // A format that reorders attributes on save turns every commit into an unreadable diff.
        var model = new FormDocument { Target = FormTarget.Web, Name = "Order" };
        var button = new FormControl { Kind = "Button", Id = "b", TabIndex = 0, Geometry = new GridGeometry { Col = 2, Row = 3 } };
        button.Properties["Text"] = "x";
        button.Properties["Enabled"] = "false";
        model.Controls.Add(button);

        var first = BlWebFormWriter.Create(model);
        var second = BlWebFormWriter.Create(model);

        Assert.That(second, Is.EqualTo(first));
        Assert.That(first.IndexOf("Id=", StringComparison.Ordinal),
            Is.LessThan(first.IndexOf("Col=", StringComparison.Ordinal)),
            "identity before geometry, geometry before properties");
    }

    // ==================================================================
    // design --check accepts the document format
    // ==================================================================

    [Test]
    public void DesignCheck_AcceptsABlwebform_AndReportsItsRefusals()
    {
        var path = Path.Combine(_dir, "Bad.blwebform");
        var xml = """
            <WebForm Name="Bad" Version="1">
              <Controls><Button Id="b" TabIndex="0" Text="{res:Missing}"/></Controls>
            </WebForm>
            """;
        File.WriteAllText(path, xml);

        var findings = DesignCheck.Check(path, xml);

        Assert.That(findings.Single().Code, Is.EqualTo(DesignCodes.ReservedResourceReference));
        Assert.That(findings.Single().IsWarning, Is.False);
    }

    [Test]
    public void DesignCheck_ReportsADegradedPropertyAsAWarning()
    {
        var path = Path.Combine(_dir, "Degraded.blwebform");
        var xml = """
            <WebForm Name="Degraded" Version="1">
              <Controls><CheckBox Id="chk" TabIndex="0" Checked="maybe"/></Controls>
            </WebForm>
            """;
        File.WriteAllText(path, xml);

        var findings = DesignCheck.Check(path, xml);

        Assert.Multiple(() =>
        {
            Assert.That(findings.Single().Code, Is.EqualTo(DesignCodes.DegradedProperty));
            Assert.That(findings.Single().IsWarning, Is.True,
                "a frozen row is information — the document is still safe to write");
            Assert.That(findings.Single().Message, Does.Contain("chk.Checked"));
        });
    }
}
