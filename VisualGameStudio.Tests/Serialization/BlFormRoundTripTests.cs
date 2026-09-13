using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Serialization;

/// <summary>
/// Task 16: the <c>.blform</c> half of the document layer — the same reader/writer, the same D9
/// tiers, and the same algebra as Task 9's <c>.blwebform</c>.
///
/// <para>⛔ This fixture is not "the web tests again with different XML". The two formats share ONE
/// reader and ONE writer (D2), so every test here is asking whether the shared code reads this
/// document in the WinForms vocabulary rather than the web one. The failure it exists to catch is
/// specific and silent: a save that writes the other format's layout attributes into the file, which
/// nothing reports and which the user sees only as controls that have moved.</para>
/// </summary>
[TestFixture]
public class BlFormRoundTripTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-form-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    /// <summary>The spec's worked example, verbatim, plus a comment and an unknown element.</summary>
    private const string LoginForm = """
        <Form Name="LoginForm" Version="1" Width="400" Height="300" Text="Sign in">
          <!-- the designer must not eat this comment -->
          <Controls>
            <Label   Id="lblUser"  Text="User"    X="20" Y="20" Width="60"  Height="23" TabIndex="0"/>
            <TextBox Id="txtUser"  X="90" Y="20"  Width="200" Height="23" Anchor="Left,Top,Right" TabIndex="1"/>
            <Button  Id="btnLogin" Text="Sign in" X="190" Y="60" Width="100" Height="30" TabIndex="2">
              <Bind Event="Click" Handler="btnLogin_Click"/>
            </Button>
          </Controls>
          <Components/>
          <Resources/>
          <FutureSection Something="42"/>
        </Form>
        """;

    private FormFile Read(string xml, string name = "LoginForm.blform")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, xml);
        return FormDocumentReader.Read(path, xml);
    }

    // ==================================================================
    // Reading — the WinForms vocabulary, not the web one
    // ==================================================================

    [Test]
    public void Read_TakesTheTargetFromTheDocument_NotFromADefault()
    {
        var form = Read(LoginForm);

        Assert.Multiple(() =>
        {
            Assert.That(form.IsRefused, Is.False, string.Join("; ", form.Diagnostics.Select(d => d.Format())));
            Assert.That(form.Model.Target, Is.EqualTo(FormTarget.WinForms));
            Assert.That(form.Model.Name, Is.EqualTo("LoginForm"));
            Assert.That(form.Model.Version, Is.EqualTo(1));
        });
    }

    [Test]
    public void Read_ReadsTheFormsOwnSizeAndCaption()
    {
        // D3's root-level divergence: a window has a client size and a caption where a page has a
        // <Layout>. On the web side these three are not read at all.
        var form = Read(LoginForm);

        Assert.Multiple(() =>
        {
            Assert.That(form.Model.Width, Is.EqualTo(400));
            Assert.That(form.Model.Height, Is.EqualTo(300));
            Assert.That(form.Model.Text, Is.EqualTo("Sign in"));
            Assert.That(form.Model.Layout, Is.Null, "a .blform has no <Layout>");
            Assert.That(form.Model.Literal, Is.Null, "a .blform has no markup to pass through");
        });
    }

    [Test]
    public void Read_GivesEveryControlPixelGeometry()
    {
        var form = Read(LoginForm);

        var user = form.Model.FindById("txtUser")!;
        var pixel = user.Geometry as PixelGeometry;

        Assert.That(pixel, Is.Not.Null, "a .blform control is positioned absolutely, not in a grid");
        Assert.Multiple(() =>
        {
            Assert.That(pixel!.X, Is.EqualTo(90));
            Assert.That(pixel.Y, Is.EqualTo(20));
            Assert.That(pixel.Width, Is.EqualTo(200));
            Assert.That(pixel.Height, Is.EqualTo(23));
            Assert.That(pixel.Anchor, Is.EqualTo("Left,Top,Right"));
            Assert.That(pixel.Dock, Is.Null);
        });
    }

    [Test]
    public void Read_KeepsTextAsAnEditableProperty_NotAsGeometry()
    {
        // ⛔ The overlap that makes a flat structural list wrong: Width is geometry on a control and
        // Text is a property on one, but Width and Text are BOTH root attributes on the form. The
        // control-level and root-level meanings are separate, and conflating them would make
        // 'Text' unreachable in the property grid.
        var form = Read(LoginForm);

        var label = form.Model.FindById("lblUser")!;

        Assert.Multiple(() =>
        {
            Assert.That(label.Properties["Text"], Is.EqualTo("User"));
            Assert.That(label.Properties.ContainsKey("Width"), Is.False, "Width is geometry here");
            Assert.That(form.TierOf("lblUser", "Text"), Is.EqualTo(PropertyTier.Canon));
        });
    }

    [Test]
    public void Read_ReadsTheBind_WithTheWinFormsEventSpelling()
    {
        // The event names differ by target and the reader does not normalise them: "Click" on
        // WinForms, "click" in the DOM. D8 emits AddHandler on one side and addEventListener on the
        // other, so a reader that lower-cased this would produce a handler that never fires.
        var form = Read(LoginForm);

        var bind = form.Model.FindById("btnLogin")!.Binds.Single();

        Assert.Multiple(() =>
        {
            Assert.That(bind.Event, Is.EqualTo("Click"));
            Assert.That(bind.Handler, Is.EqualTo("btnLogin_Click"));
        });
    }

    [Test]
    public void Read_TreatsAGridAttributeAsUnknown_RatherThanAsGeometry()
    {
        // ⛔ The geometry is chosen by the document's TARGET, never by sniffing which attributes are
        // present. Sniffing would read this Col as a grid cell, and the next save would emit grid
        // geometry into a document whose every other control is absolute — half of one format and
        // half of the other, produced by a save nobody asked to be a conversion.
        var form = Read("""
            <Form Name="F" Version="1">
              <Controls><Button Id="b" X="10" Y="10" Col="3" TabIndex="0"/></Controls>
            </Form>
            """, "F.blform");

        var button = form.Model.FindById("b")!;

        Assert.Multiple(() =>
        {
            Assert.That(button.Geometry, Is.TypeOf<PixelGeometry>());
            Assert.That(button.UnknownAttributes["Col"], Is.EqualTo("3"),
                "the other format's vocabulary is an unknown attribute, which round-trips");
        });
    }

    [Test]
    public void Read_TreatsALayoutElementInABlformAsAnUnknownChild()
    {
        var form = Read("""
            <Form Name="F" Version="1">
              <Layout Kind="Grid" Cols="1fr"/>
              <Controls/>
            </Form>
            """, "F.blform");

        Assert.Multiple(() =>
        {
            Assert.That(form.Model.Layout, Is.Null);
            Assert.That(form.Model.UnknownChildren.Select(e => e.Name.LocalName), Does.Contain("Layout"));
        });
    }

    // ==================================================================
    // The file name and the root must agree
    // ==================================================================

    [Test]
    public void Read_RefusesABlformWhoseRootIsAWebForm()
    {
        // ⛔ Neither side can be believed over the other. Trust the root and the writer emits web
        // geometry into a file the project system treats as a WinForms form; trust the extension and
        // every Col/Row in it reads as an unknown attribute and the canvas comes up empty. Both are
        // invisible until the user saves, and neither is recoverable afterwards.
        var form = Read("""<WebForm Name="F" Version="1"><Controls/></WebForm>""", "F.blform");

        Assert.Multiple(() =>
        {
            Assert.That(form.IsRefused, Is.True);
            Assert.That(form.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.MalformedDocument));
            Assert.That(form.Diagnostics.Single().Message, Does.Contain(".blform"));
        });
    }

    [Test]
    public void Read_RefusesABlwebformWhoseRootIsAForm()
    {
        var form = Read("""<Form Name="F" Version="1"><Controls/></Form>""", "F.blwebform");

        Assert.That(form.IsRefused, Is.True);
        Assert.That(form.Diagnostics.Single().Message, Does.Contain(".blwebform"));
    }

    [Test]
    public void Read_RefusesARootThatIsNeitherFormat()
    {
        var form = Read("""<Window Name="F"><Controls/></Window>""", "F.blform");

        Assert.That(form.IsRefused, Is.True);
        Assert.That(form.Diagnostics.Single().Message, Does.Contain("<Form>").And.Contain("<WebForm>"));
    }

    [Test]
    public void Read_LetsTheRootDecide_WhenThePathCarriesNoFormExtension()
    {
        // The recognizer and the tests both hand this reader text with an arbitrary path. With no
        // extension to claim a format, the document's own root is the only evidence there is.
        var form = FormDocumentReader.Read("scratch", """<Form Name="F" Version="1"><Controls/></Form>""");

        Assert.Multiple(() =>
        {
            Assert.That(form.IsRefused, Is.False);
            Assert.That(form.Model.Target, Is.EqualTo(FormTarget.WinForms));
        });
    }

    // ==================================================================
    // D9 tiers — identical rules, WinForms document
    // ==================================================================

    [Test]
    public void Tier_IsDegraded_ForOneBadValue_AndTheRestOfTheControlStaysCanon()
    {
        var form = Read("""
            <Form Name="F" Version="1">
              <Controls><CheckBox Id="chk" X="10" Y="10" TabIndex="0" Text="Remember" Checked="maybe"/></Controls>
            </Form>
            """, "F.blform");

        Assert.Multiple(() =>
        {
            Assert.That(form.TierOf("chk", "Checked"), Is.EqualTo(PropertyTier.Degraded));
            Assert.That(form.TierOf("chk", "Text"), Is.EqualTo(PropertyTier.Canon),
                "one bad value costs one row, not the control");
            Assert.That(form.DegradedReason("chk", "Checked"), Does.Contain("Bool"));
            Assert.That(form.IsRefused, Is.False, "Degraded is not Refused");
        });
    }

    [Test]
    public void Tier_IsUnknown_ForAnAttributeTheCatalogDoesNotKnow()
    {
        var form = Read("""
            <Form Name="F" Version="1">
              <Controls><Button Id="b" X="0" Y="0" TabIndex="0" FlatStyle="Popup"/></Controls>
            </Form>
            """, "F.blform");

        Assert.That(form.TierOf("b", "FlatStyle"), Is.EqualTo(PropertyTier.Unknown));
    }

    [Test]
    public void Read_ReportsANonIntegerStructuralValue_RatherThanThrowing()
    {
        // ⛔ (int?) on an XAttribute THROWS on a non-integer. X="20px" is the likeliest way for a
        // hand-edited .blform to carry one — a CSS habit applied to the wrong format.
        FormFile form = null!;
        Assert.DoesNotThrow(() => form = Read("""
            <Form Name="Bad" Version="1" Width="wide">
              <Controls><Button Id="b" X="20px" Y="10" TabIndex="one"/></Controls>
            </Form>
            """, "Bad.blform"));

        Assert.Multiple(() =>
        {
            Assert.That(form.Model.FindById("b")!.TabIndex, Is.Zero, "unparseable reads as absent");
            Assert.That(((PixelGeometry)form.Model.FindById("b")!.Geometry!).X, Is.Zero);
            Assert.That(form.Model.Width, Is.Null, "an unparseable form width is not a width");
            Assert.That(form.IsRefused, Is.False);
        });
    }

    // ==================================================================
    // Control ids — they become field names in the user's own file
    // ==================================================================

    [Test]
    public void AControlWithNoId_IsRefused()
    {
        // ⛔⛔ Until this existed, FormDocument.IsLegalControlId had NO caller outside its own
        // tests, and this document passed `design --check` with zero findings while the region
        // writer generated `Private  As Button` and `Me.Controls.Add()` into the user's .bas.
        var form = Read("""
            <Form Name="F" Version="1">
              <Controls><Button Id="" X="10" Y="10" TabIndex="0"/></Controls>
            </Form>
            """, "F.blform");

        Assert.Multiple(() =>
        {
            Assert.That(form.IsRefused, Is.True);
            Assert.That(form.Diagnostics.Select(d => d.Code), Does.Contain(DesignCodes.IllegalControlId));
        });
    }

    [Test]
    public void AControlWithAnIdThatIsNotAnIdentifier_IsRefused()
    {
        // `my-button = New Button()` is a syntax error in a file the user owns.
        var form = Read("""
            <Form Name="F" Version="1">
              <Controls><Button Id="my-button" X="10" Y="10" TabIndex="0"/></Controls>
            </Form>
            """, "F.blform");

        Assert.That(form.Diagnostics.Select(d => d.Code), Does.Contain(DesignCodes.IllegalControlId));
    }

    [Test]
    public void AnUnderscoreIsFineInAControlId()
    {
        // ⚠ The form's NAME may not contain one (it becomes a type name and falls out of the
        // PascalCase heuristic), but a control id is an ordinary identifier.
        var form = Read("""
            <Form Name="F" Version="1">
              <Controls><Button Id="btn_login" X="10" Y="10" TabIndex="0"/></Controls>
            </Form>
            """, "F.blform");

        Assert.That(form.IsRefused, Is.False,
            string.Join("; ", form.Diagnostics.Select(d => d.Format())));
    }

    [Test]
    public void TwoControlsSharingAnId_AreRefused()
    {
        var form = Read("""
            <Form Name="F" Version="1">
              <Controls>
                <Button Id="dup" X="0" Y="0" TabIndex="0"/>
                <Button Id="dup" X="0" Y="30" TabIndex="1"/>
              </Controls>
            </Form>
            """, "F.blform");

        Assert.That(form.Diagnostics.Select(d => d.Code), Does.Contain(DesignCodes.DuplicateControlId));
    }

    [Test]
    public void ADuplicateIsFoundAcrossContainers_NotJustAmongSiblings()
    {
        // ⛔ The generated fields are all members of ONE class, so a Button inside a Panel collides
        // with a Button on the form just as surely as two siblings do.
        var form = Read("""
            <Form Name="F" Version="1">
              <Controls>
                <Button Id="dup" X="0" Y="0" TabIndex="0"/>
                <Panel Id="pnl" X="0" Y="40" Width="100" Height="100" TabIndex="1">
                  <Button Id="dup" X="5" Y="5" TabIndex="2"/>
                </Panel>
              </Controls>
            </Form>
            """, "F.blform");

        Assert.That(form.Diagnostics.Select(d => d.Code), Does.Contain(DesignCodes.DuplicateControlId));
    }

    // ==================================================================
    // The algebra — the properties the whole design rests on
    // ==================================================================

    [Test]
    public void Algebra_RoundTripIsByteIdentical()
    {
        var form = Read(LoginForm);

        Assert.That(FormDocumentWriter.Write(form), Is.EqualTo(LoginForm));
    }

    [Test]
    public void Algebra_ANoOpPatchWritesNothing()
    {
        var path = Path.Combine(_dir, "LoginForm.blform");
        File.WriteAllText(path, LoginForm);
        var form = FormDocumentReader.Read(path, LoginForm);

        // Setting a property to the value it already has is the no-op the designer performs
        // constantly — every selection change re-pushes the property grid's values.
        form.Model.FindById("btnLogin")!.Properties["Text"] = "Sign in";

        Assert.Multiple(() =>
        {
            Assert.That(FormDocumentWriter.Save(form), Is.False, "nothing differed, so nothing is written");
            Assert.That(File.ReadAllText(path), Is.EqualTo(LoginForm));
        });
    }

    [Test]
    public void Algebra_ReadAfterApply_EqualsApplyAfterRead()
    {
        // Read∘Apply == Apply∘Read, on the property that actually moves: geometry.
        var a = Read(LoginForm);
        ((PixelGeometry)a.Model.FindById("btnLogin")!.Geometry!).X = 250;
        var applyThenRead = FormDocumentWriter.Write(a);

        var b = Read(applyThenRead);
        ((PixelGeometry)b.Model.FindById("btnLogin")!.Geometry!).X = 250;
        var readThenApply = FormDocumentWriter.Write(b);

        Assert.That(readThenApply, Is.EqualTo(applyThenRead));
    }

    // ==================================================================
    // Writing
    // ==================================================================

    [Test]
    public void Write_KeepsEverythingItDoesNotOwn_WhenSomethingDidChange()
    {
        var form = Read(LoginForm);
        form.Model.FindById("btnLogin")!.Properties["Text"] = "Log in";

        var after = FormDocumentWriter.Write(form);

        Assert.Multiple(() =>
        {
            Assert.That(after, Does.Contain("Log in"));
            Assert.That(after, Does.Contain("<!-- the designer must not eat this comment -->"),
                "the comment is not the designer's to remove");
            Assert.That(after, Does.Contain("FutureSection"), "an unknown element round-trips");
            Assert.That(after, Does.Contain("""Anchor="Left,Top,Right" """.TrimEnd()),
                "an untouched control's anchoring survives");
            Assert.That(after, Does.Contain("""Text="Sign in" """.TrimEnd()),
                "the FORM's caption is not the button's Text and must not follow it");
            Assert.That(after, Does.Contain("btnLogin_Click"), "an untouched binding survives");
        });
    }

    [Test]
    public void Write_MovesAControl_InPixels()
    {
        var form = Read(LoginForm);
        var geometry = (PixelGeometry)form.Model.FindById("lblUser")!.Geometry!;
        geometry.X = 30;
        geometry.Y = 45;

        var after = FormDocumentWriter.Write(form);

        Assert.Multiple(() =>
        {
            Assert.That(after, Does.Contain("""X="30" """.TrimEnd()));
            Assert.That(after, Does.Contain("""Y="45" """.TrimEnd()));
            Assert.That(after, Does.Not.Contain("Col="), "a .blform never gains grid geometry");
            Assert.That(after, Does.Not.Contain("<Layout"), "nor a layout");
        });
    }

    [Test]
    public void Write_ResizesTheFormItself()
    {
        var form = Read(LoginForm);
        form.Model.Width = 640;
        form.Model.Text = "Log in";

        var after = FormDocumentWriter.Write(form);

        Assert.Multiple(() =>
        {
            Assert.That(after, Does.Contain("""Width="640" """.TrimEnd()));
            Assert.That(after, Does.Contain("""Text="Log in" """.TrimEnd()));
            Assert.That(after, Does.Contain("""Height="300" """.TrimEnd()), "an untouched dimension stays");
        });
    }

    [Test]
    public void Write_AddsANewControl_WithPixelGeometry()
    {
        var form = Read(LoginForm);

        var added = new FormControl
        {
            Kind = "CheckBox",
            Id = "chkRemember",
            TabIndex = 3,
            Geometry = new PixelGeometry { X = 90, Y = 100, Width = 120, Height = 24 }
        };
        added.Properties["Text"] = "Remember me";
        form.Model.Controls.Add(added);

        var after = FormDocumentWriter.Write(form);
        var reread = Read(after);

        var found = reread.Model.FindById("chkRemember");
        Assert.That(found, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(((PixelGeometry)found!.Geometry!).X, Is.EqualTo(90));
            Assert.That(((PixelGeometry)found.Geometry!).Height, Is.EqualTo(24));
            Assert.That(found.Properties["Text"], Is.EqualTo("Remember me"));
            Assert.That(reread.Model.Controls, Has.Count.EqualTo(4));
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
    public void Write_DoesNotAddDefaultsToAHandWrittenDocumentThatOmittedThem()
    {
        // ⛔ The reader reads an absent X/Y/Width/Height as 0, so writing "0" back materialises four
        // attributes on every control of a hand-authored document the first time anything else on
        // the form is touched. "A no-op patch writes nothing" would then hold only for documents
        // this writer had already produced.
        var original = """
            <Form Name="Sparse" Version="1">
              <Controls>
                <Label Id="a" Text="Hi"/>
                <Label Id="b" Text="There"/>
              </Controls>
            </Form>
            """;
        var form = Read(original, "Sparse.blform");
        form.Model.FindById("a")!.Properties["Text"] = "Hello";

        var after = FormDocumentWriter.Write(form);

        Assert.Multiple(() =>
        {
            Assert.That(after, Does.Contain("Hello"));
            Assert.That(after, Does.Not.Contain("X="), "an omitted position stays omitted");
            Assert.That(after, Does.Not.Contain("Height="));
            Assert.That(after, Does.Not.Contain("TabIndex="));
        });
    }

    [Test]
    public void Write_PreservesAFormWidthItCouldNotParse()
    {
        // A null model Width means "the document did not say" — absent, or present and unreadable.
        // Writing null as a removal would delete something the user wrote and we never understood.
        var original = """
            <Form Name="Odd" Version="1" Width="400px" Height="300">
              <Controls><Label Id="a" Text="Hi" X="5" Y="5"/></Controls>
            </Form>
            """;
        var form = Read(original, "Odd.blform");
        form.Model.FindById("a")!.Properties["Text"] = "Hello";

        var after = FormDocumentWriter.Write(form);

        Assert.That(after, Does.Contain("""Width="400px" """.TrimEnd()));
    }

    [Test]
    public void Write_RefusesToTouchARefusedDocument()
    {
        // The mismatch refusal returns a full tree and an EMPTY model. Writing from it would replace
        // a valid web document with an empty WinForms one.
        var original = """<WebForm Name="Real" Version="1"><Controls><Button Id="b" Col="1" TabIndex="0"/></Controls></WebForm>""";
        var path = Path.Combine(_dir, "Real.blform");
        File.WriteAllText(path, original);
        var form = FormDocumentReader.Read(path, original);

        Assert.Multiple(() =>
        {
            Assert.That(form.IsRefused, Is.True);
            Assert.That(FormDocumentWriter.Write(form), Is.EqualTo(original));
            Assert.That(FormDocumentWriter.Save(form), Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo(original));
        });
    }

    [Test]
    public void Write_CalledTwiceOnTheSameInstance_DoesNotRevertTheEdit()
    {
        var form = Read(LoginForm);
        form.Model.FindById("btnLogin")!.Properties["Text"] = "Log in";

        var first = FormDocumentWriter.Write(form);
        var second = FormDocumentWriter.Write(form);

        Assert.That(second, Is.EqualTo(first), "the tree is mutated in place; the second call is a no-op");
        Assert.That(second, Does.Contain("Log in"));
    }

    // ==================================================================
    // Creating
    // ==================================================================

    [Test]
    public void Create_ProducesABlformThatReadsBackIdentically()
    {
        var model = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "NewForm", Width = 800, Height = 450, Text = "New Form"
        };
        var button = new FormControl
        {
            Kind = "Button",
            Id = "btnOk",
            TabIndex = 0,
            Geometry = new PixelGeometry { X = 10, Y = 20, Width = 80, Height = 24, Anchor = "Bottom,Right" }
        };
        button.Properties["Text"] = "OK";
        model.Controls.Add(button);

        var text = FormDocumentWriter.Create(model);
        var reread = Read(text, "NewForm.blform");

        Assert.That(reread.IsRefused, Is.False, string.Join("; ", reread.Diagnostics.Select(d => d.Format())));
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("<Form "), "the element name IS the format");
            Assert.That(reread.Model.Target, Is.EqualTo(FormTarget.WinForms));
            Assert.That(reread.Model.Width, Is.EqualTo(800));
            Assert.That(reread.Model.Text, Is.EqualTo("New Form"));
            Assert.That(((PixelGeometry)reread.Model.FindById("btnOk")!.Geometry!).Anchor,
                Is.EqualTo("Bottom,Right"));
            Assert.That(text, Does.Not.Contain("<Literal"), "a .blform has no literal markup");
            Assert.That(text, Does.Not.Contain("<Layout"));
        });
    }

    [Test]
    public void Scaffold_CreatesAWinFormsPairWhoseDocumentReadsBackClean()
    {
        var scaffold = FormScaffolder.Create("MainForm", FormTarget.WinForms);

        var form = FormDocumentReader.Read(scaffold.DocumentFileName, scaffold.DocumentText);

        Assert.That(form.IsRefused, Is.False, string.Join("; ", form.Diagnostics.Select(d => d.Format())));
        Assert.Multiple(() =>
        {
            Assert.That(scaffold.DocumentFileName, Is.EqualTo("MainForm.blform"));
            Assert.That(form.Model.Target, Is.EqualTo(FormTarget.WinForms));
            Assert.That(form.Model.Text, Is.EqualTo("MainForm"), "a new window gets its own name as a caption");
            Assert.That(form.Model.Width, Is.Not.Null, "and a size, or it opens at whatever WinForms defaults to");
        });
    }

    // ==================================================================
    // design --check accepts the other document format too
    // ==================================================================

    [Test]
    public void DesignCheck_AcceptsABlform_AndReportsItsRefusals()
    {
        // ⛔ The regression this pins is silent: with only ".blwebform" in the dispatch, a .blform
        // went to the SOURCE checker, which lexed XML as BasicLang, found no form shape, and
        // reported BL8005 — a clean-looking answer to a question nobody asked.
        var path = Path.Combine(_dir, "Bad.blform");
        var xml = """
            <Form Name="Bad" Version="1">
              <Controls><Button Id="b" X="0" Y="0" TabIndex="0" Text="{res:Missing}"/></Controls>
            </Form>
            """;
        File.WriteAllText(path, xml);

        var findings = DesignCheck.Check(path, xml);

        Assert.Multiple(() =>
        {
            Assert.That(findings.Single().Code, Is.EqualTo(DesignCodes.ReservedResourceReference));
            Assert.That(findings.Single().IsWarning, Is.False);
        });
    }

    [Test]
    public void DesignCheck_ReportsADegradedPropertyInABlformAsAWarning()
    {
        var path = Path.Combine(_dir, "Degraded.blform");
        var xml = """
            <Form Name="Degraded" Version="1">
              <Controls><CheckBox Id="chk" X="0" Y="0" TabIndex="0" Checked="maybe"/></Controls>
            </Form>
            """;
        File.WriteAllText(path, xml);

        var findings = DesignCheck.Check(path, xml);

        Assert.Multiple(() =>
        {
            Assert.That(findings.Single().Code, Is.EqualTo(DesignCodes.DegradedProperty));
            Assert.That(findings.Single().IsWarning, Is.True);
            Assert.That(findings.Single().Message, Does.Contain("chk.Checked"));
        });
    }
}
