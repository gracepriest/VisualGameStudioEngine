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
public class FormDocumentRoundTripTests
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

    private FormFile Read(string xml, string name = "LoginForm.blwebform")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, xml);
        return FormDocumentReader.Read(path, xml);
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
        FormFile form = null!;
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

        Assert.That(FormDocumentWriter.Write(form), Is.EqualTo(xml),
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

        Assert.That(FormDocumentWriter.Write(form), Is.EqualTo(LoginForm),
            "reading and writing with no edit must reproduce the file exactly — comments, the " +
            "unknown <FutureSection>, attribute spelling and indentation included");
    }

    [Test]
    public void Algebra_ANoOpPatchWritesNothing()
    {
        var path = Path.Combine(_dir, "NoOp.blwebform");
        File.WriteAllText(path, LoginForm);
        var before = File.GetLastWriteTimeUtc(path);

        var form = FormDocumentReader.Read(path, LoginForm);
        var wrote = FormDocumentWriter.Save(form);

        Assert.Multiple(() =>
        {
            Assert.That(wrote, Is.False, "nothing changed, so nothing may be written");
            Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(before),
                "a save that touches the file on every canvas tick makes every Build/F5 look like an edit");
        });
    }

    // ==================================================================
    // Numbers never carry the author's culture into the document
    // ==================================================================

    private const string NegativeWinForm = """
        <Form Name="F" Version="1" Width="400" Height="300">
          <Controls>
            <Button Id="btn" TabIndex="0" X="-5" Y="-3" Width="75" Height="23"/>
          </Controls>
        </Form>
        """;

    /// <summary>
    /// ⛔⛔ The writer wrote <c>value.ToString()</c>: under sv-SE the file held <c>X="−5"</c> (U+2212),
    /// which round-tripped on the author's machine and silently fell to 0 on en-US / CI.
    /// </summary>
    [Test]
    [SetCulture("sv-SE")]
    public void Write_ANegativeCoordinate_UnderAUnicodeMinusCulture_UsesAnAsciiHyphen()
    {
        UnicodeMinusCulture.Require();
        var form = Read(NegativeWinForm, "F.blform");
        var pixel = (PixelGeometry)form.Model.FindById("btn")!.Geometry!;
        pixel.X = -12;
        pixel.Y = -34;

        var written = FormDocumentWriter.Write(form);

        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("X=\"-12\""));
            Assert.That(written, Does.Contain("Y=\"-34\""));
            Assert.That(written, Does.Not.Contain(UnicodeMinusCulture.Minus));
        });
    }

    [Test]
    [SetCulture("sv-SE")]
    public void Write_ANewNegativeSpan_UnderAUnicodeMinusCulture_UsesAnAsciiHyphen()
    {
        // SetOptionalIntAttribute's path — a span the designer never writes negative, but the writer
        // must not be the place a culture decides the text.
        UnicodeMinusCulture.Require();
        var form = Read(LoginForm);
        ((GridGeometry)form.Model.FindById("btnLogin")!.Geometry!).ColSpan = -2;

        Assert.That(FormDocumentWriter.Write(form), Does.Contain("ColSpan=\"-2\""));
    }

    [Test]
    [SetCulture("sv-SE")]
    public void Read_AnAsciiNegative_UnderAUnicodeMinusCulture_IsThatNumber()
    {
        UnicodeMinusCulture.Require();
        var pixel = (PixelGeometry)Read(NegativeWinForm, "F.blform").Model.FindById("btn")!.Geometry!;

        Assert.Multiple(() =>
        {
            Assert.That(pixel.X, Is.EqualTo(-5));
            Assert.That(pixel.Y, Is.EqualTo(-3));
        });
    }

    /// <summary>
    /// ⛔ The document means the same number on every machine: U+2212 is not a minus in it, even on
    /// the one machine whose culture spells negatives that way.
    /// </summary>
    [Test]
    [SetCulture("sv-SE")]
    public void Read_AUnicodeMinus_IsNotANumber_EvenUnderTheCultureThatWritesIt()
    {
        UnicodeMinusCulture.Require();
        var xml = NegativeWinForm.Replace("X=\"-5\"", $"X=\"{UnicodeMinusCulture.Minus}5\"");

        var pixel = (PixelGeometry)Read(xml, "F.blform").Model.FindById("btn")!.Geometry!;

        Assert.That(pixel.X, Is.Not.EqualTo(-5), "read culture-free, exactly as an en-US machine reads it");
    }

    /// <summary>
    /// ⛔ The writer's no-op comparison uses the READER's parser, so a save that changed nothing writes
    /// nothing — for an ASCII negative, a spelled-out one, and a U+2212 one the reader refused alike.
    /// </summary>
    [TestCase("X=\"-5\"")]
    [TestCase("X=\"-005\"")]
    [TestCase("X=\" -5 \"")]
    [TestCase("X=\"<MINUS>5\"")]
    [SetCulture("sv-SE")]
    public void Algebra_ANoOpSave_UnderAUnicodeMinusCulture_IsByteIdentical(string attribute)
    {
        UnicodeMinusCulture.Require();
        var xml = NegativeWinForm.Replace("X=\"-5\"", attribute.Replace("<MINUS>", UnicodeMinusCulture.Minus));

        Assert.That(FormDocumentWriter.Write(Read(xml, "F.blform")), Is.EqualTo(xml));
    }

    [Test]
    public void Algebra_ReadAfterApply_EqualsApplyAfterRead()
    {
        // Read∘Apply == Apply∘Read. Both sides make the same edit; the results must agree, or the
        // reader and the writer disagree about what the document means and corruption is slow.
        var edited = Read(LoginForm);
        edited.Model.FindById("btnLogin")!.Properties["Text"] = "Log in";
        var applyThenRead = FormDocumentReader.Read(edited.FilePath, FormDocumentWriter.Write(edited));

        var readThenApply = Read(LoginForm);
        readThenApply.Model.FindById("btnLogin")!.Properties["Text"] = "Log in";

        Assert.Multiple(() =>
        {
            Assert.That(applyThenRead.Model.FindById("btnLogin")!.Properties["Text"], Is.EqualTo("Log in"));
            Assert.That(applyThenRead.Model.Controls.Select(c => c.Id),
                Is.EqualTo(readThenApply.Model.Controls.Select(c => c.Id)));
            Assert.That(FormDocumentWriter.Write(applyThenRead), Is.EqualTo(FormDocumentWriter.Write(readThenApply)),
                "writing either side must give the same document");
        });
    }

    [Test]
    public void Write_KeepsEverythingItDoesNotOwn_WhenSomethingDidChange()
    {
        var form = Read(LoginForm);
        form.Model.FindById("btnLogin")!.Properties["Text"] = "Log in";

        var after = FormDocumentWriter.Write(form);

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
        var first = FormDocumentWriter.Write(form);

        var reloaded = FormDocumentReader.Read(form.FilePath, first);
        Assert.Multiple(() =>
        {
            Assert.That(FormDocumentWriter.Write(reloaded), Is.EqualTo(first),
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

        var after = FormDocumentWriter.Write(form);

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

        var after = FormDocumentWriter.Write(form);
        var reloaded = FormDocumentReader.Read(form.FilePath, after);

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
    // Regressions found by review — each of these lost user content
    // ==================================================================

    [Test]
    public void Write_CalledTwiceOnTheSameInstance_DoesNotRevertTheEdit()
    {
        // ⛔ ApplyToDocument mutates the tree IN PLACE. Comparing a second write against the
        // ORIGINAL text made it see "nothing changed" and return the pre-edit document — so a second
        // Save wrote the old content back over the saved one. The exact opposite of the no-op it is
        // supposed to be, and every fixture dodged it by re-reading between writes.
        var form = Read(LoginForm);
        form.Model.FindById("btnLogin")!.Properties["Text"] = "Log in";

        var first = FormDocumentWriter.Write(form);
        var second = FormDocumentWriter.Write(form);

        Assert.Multiple(() =>
        {
            Assert.That(first, Does.Contain("Log in"));
            Assert.That(second, Is.EqualTo(first), "the second write must not resurrect the old text");
            Assert.That(second, Does.Not.Contain("Sign in"));
        });
    }

    [Test]
    public void Save_CalledTwice_DoesNotWriteThePreEditDocumentBack()
    {
        var path = Path.Combine(_dir, "Twice.blwebform");
        File.WriteAllText(path, LoginForm);
        var form = FormDocumentReader.Read(path, LoginForm);
        form.Model.FindById("btnLogin")!.Properties["Text"] = "Log in";

        FormDocumentWriter.Save(form);
        var afterFirst = File.ReadAllText(path);
        var wroteAgain = FormDocumentWriter.Save(form);

        Assert.Multiple(() =>
        {
            Assert.That(wroteAgain, Is.False, "nothing changed, so nothing may be written");
            Assert.That(File.ReadAllText(path), Is.EqualTo(afterFirst));
            Assert.That(File.ReadAllText(path), Does.Contain("Log in"));
        });
    }

    [Test]
    public void Write_RefusesToTouchARefusedDocument()
    {
        // ⛔ The refusal paths return the full parsed tree but an EMPTY model — reading stops before
        // Name, Version, controls, layout and literal are populated. Writing from that model deleted
        // every control, downgraded the version to 1, stamped the filename over the document's name
        // and dropped the <Literal>. Refusing a document to AVOID a lossy save and then performing
        // exactly that save is the worst outcome available.
        var newer = """
            <WebForm Name="Future" Version="99">
              <Controls><Button Id="btnKeep" TabIndex="0" Text="Keep me"/></Controls>
              <Literal><![CDATA[<p>keep</p>]]></Literal>
            </WebForm>
            """;
        var form = Read(newer, "Future.blwebform");

        Assert.That(form.IsRefused, Is.True, "sanity: a newer version is refused");

        Assert.Multiple(() =>
        {
            Assert.That(FormDocumentWriter.Write(form), Is.EqualTo(newer),
                "not one byte of a refused document may change");
            Assert.That(FormDocumentWriter.Save(form), Is.False, "and nothing may reach the disk");
        });
    }

    [Test]
    public void Write_KeepsAReservedResourceReference()
    {
        // The reader skipped adding {res:…} to the model, so the writer's dropped-catalog-property
        // sweep DELETED it — the document refused specifically to preserve the reference was the one
        // that lost it.
        var xml = """
            <WebForm Name="Res" Version="1">
              <Controls><Button Id="b" TabIndex="0" Text="{res:SignIn}"/></Controls>
            </WebForm>
            """;
        var form = Read(xml, "Res.blwebform");

        Assert.Multiple(() =>
        {
            Assert.That(form.Model.FindById("b")!.Properties["Text"], Is.EqualTo("{res:SignIn}"),
                "the value must reach the model or the writer cannot know to keep it");
            Assert.That(FormDocumentWriter.Write(form), Does.Contain("{res:SignIn}"));
        });
    }

    [Test]
    public void Read_ReportsANonIntegerStructuralValue_RatherThanThrowing()
    {
        // ⛔ (int?) on an XAttribute THROWS on a non-integer. TabIndex="one" escaped the reader, the
        // check's IOException-only catch, and surfaced as "design failed: the input string was not
        // in a correct format", exit 2 — a tool failure. The asymmetry was the tell: a bad CATALOG
        // value got a careful Degraded tier while a bad STRUCTURAL value crashed.
        FormFile form = null!;
        Assert.DoesNotThrow(() => form = Read("""
            <WebForm Name="Bad" Version="1">
              <Controls><Button Id="b" TabIndex="one" Col="two"/></Controls>
            </WebForm>
            """, "Bad.blwebform"));

        Assert.Multiple(() =>
        {
            Assert.That(form.Model.FindById("b")!.TabIndex, Is.Zero, "unparseable reads as absent");
            Assert.That(form.IsRefused, Is.False);
        });
    }

    [Test]
    public void Algebra_ANoOpWriteDoesNotRewriteAnUnparseableStructuralValue()
    {
        // ⛔⛔ The byte-identity guarantee, at its sharpest. The reader cannot parse Version="1.0"
        // or TabIndex="two", so the MODEL holds a default it invented — 1 and 0. The writer then
        // saw an attribute present, wrote the model value over it, and silently turned the user's
        // text into that default: a no-op save downgraded the version and RESET the tab order.
        // No diagnostic, no Degraded row, and the file changed on a save the user did not make.
        var original = """
            <WebForm Name="F" Version="1.0">
              <Controls><Button Id="b" TabIndex="two" Col="0"/></Controls>
            </WebForm>
            """;
        var form = Read(original, "F.blwebform");

        var written = FormDocumentWriter.Write(form);

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.EqualTo(original), "a no-op write must change nothing at all");
            Assert.That(written, Does.Contain("""Version="1.0" """.TrimEnd()));
            Assert.That(written, Does.Contain("""TabIndex="two" """.TrimEnd()));
        });
    }

    [Test]
    public void Algebra_AGenuineEditStillWrites_OverAnUnparseableValue()
    {
        // ⚠ The other half, or the fix would be "never write TabIndex again". Renumbering the tab
        // order is a real designer action and must reach the document even when the value it
        // replaces was unreadable.
        var form = Read("""
            <WebForm Name="F" Version="1">
              <Controls><Button Id="b" TabIndex="two" Col="0"/></Controls>
            </WebForm>
            """, "F.blwebform");

        form.Model.FindById("b")!.TabIndex = 5;

        Assert.That(FormDocumentWriter.Write(form), Does.Contain("""TabIndex="5" """.TrimEnd()));
    }

    [Test]
    public void Read_DoesNotThrow_OnANonIntegerVersion()
    {
        Assert.DoesNotThrow(() => Read("""<WebForm Name="V" Version="1.0"><Controls/></WebForm>""", "V.blwebform"));
    }

    [Test]
    public void Write_DoesNotAddDefaultsToAHandWrittenDocumentThatOmittedThem()
    {
        // ⛔ The reader fills Name from the FILENAME, Version from SupportedVersion, TabIndex/Col/Row
        // from 0 and Layout.Kind from Grid. Writing those back unconditionally rewrote a
        // hand-authored document that deliberately omitted them — so "a no-op patch writes nothing"
        // was true only for documents this writer had already produced, which is exactly the set
        // that does not need the guarantee.
        var sparse = """
            <WebForm>
              <Layout Cols="1fr"/>
              <Controls><Button Id="b"/></Controls>
            </WebForm>
            """;
        var form = Read(sparse, "b.blwebform");

        var after = FormDocumentWriter.Write(form);

        Assert.Multiple(() =>
        {
            Assert.That(after, Is.EqualTo(sparse), "a no-op save on a sparse document writes nothing");
            Assert.That(after, Does.Not.Contain("TabIndex"));
            Assert.That(after, Does.Not.Contain("Version"));
            Assert.That(after, Does.Not.Contain("Kind"));
        });
    }

    [Test]
    public void Write_StillAddsAPropertyWhoseValueIsNotTheDefault()
    {
        // The other half: suppressing defaults must not suppress a real value.
        var form = Read("""
            <WebForm>
              <Controls><Button Id="b"/></Controls>
            </WebForm>
            """, "b.blwebform");
        form.Model.FindById("b")!.TabIndex = 3;

        Assert.That(FormDocumentWriter.Write(form), Does.Contain("""TabIndex="3" """.TrimEnd()));
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

        var text = FormDocumentWriter.Create(model);
        var reloaded = FormDocumentReader.Read(Path.Combine(_dir, "Fresh.blwebform"), text);

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

        var first = FormDocumentWriter.Create(model);
        var second = FormDocumentWriter.Create(model);

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
