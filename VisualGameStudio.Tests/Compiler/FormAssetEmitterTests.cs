using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 12: the build-time markup/CSS emitter (D6, D7).
///
/// <para>⛔ <b>Exit 0 is not a gate when the deliverable is files.</b> These assert the files exist,
/// carry the expected ids, and contain no unlowered BasicLang and no unsubstituted placeholders —
/// the <c>AssertSiteWasWritten</c> discipline. A test that only checks the emitter returned without
/// throwing proves nothing about what landed on disk.</para>
/// </summary>
[TestFixture]
public class FormAssetEmitterTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    private static FormDocument LoginForm()
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "LoginForm" };
        form.Layout = new FormLayout
        {
            Kind = FormLayoutKind.Grid, Cols = "120px,1fr", Rows = "auto,auto", Gap = "8px"
        };

        var label = new FormControl { Kind = "Label", Id = "lblUser", TabIndex = 0,
            Geometry = new GridGeometry { Col = 0, Row = 0 } };
        label.Properties["Text"] = "User";

        var box = new FormControl { Kind = "TextBox", Id = "txtUser", TabIndex = 1,
            Geometry = new GridGeometry { Col = 1, Row = 0 } };

        var button = new FormControl { Kind = "Button", Id = "btnLogin", TabIndex = 2,
            Geometry = new GridGeometry { Col = 1, Row = 1 } };
        button.Properties["Text"] = "Sign in";
        button.Binds.Add(new FormBind { Event = "click", Handler = "btnLogin_Click" });

        form.Controls.Add(label);
        form.Controls.Add(box);
        form.Controls.Add(button);
        form.Literal = """<p class="hint">Use your work account.</p>""";
        return form;
    }

    /// <summary>The file-deliverable gate, modelled on TemplateBuildSweepTests.AssertSiteWasWritten.</summary>
    private void AssertPageWasWritten(string formName)
    {
        var html = Path.Combine(_dir, formName + ".html");
        var css = Path.Combine(_dir, formName + ".css");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(html), Is.True, $"{formName}.html was not written");
            Assert.That(File.Exists(css), Is.True, $"{formName}.css was not written");
        });

        var markup = File.ReadAllText(html);
        Assert.Multiple(() =>
        {
            Assert.That(markup, Does.Not.Contain("End Sub"), "BasicLang text leaked into the page");
            Assert.That(markup, Does.Not.Contain("{{"), "an unsubstituted placeholder reached the page");
            Assert.That(markup, Does.Not.Contain("AddressOf"), "wiring code leaked into the markup");
            Assert.That(markup, Does.StartWith("<!DOCTYPE html>"));
        });
    }

    // ==================================================================
    // What lands on disk
    // ==================================================================

    [Test]
    public void Emit_WritesAPageAndAStylesheetPerForm()
    {
        var written = FormAssetEmitter.Emit(_dir, "Site.js", new[] { LoginForm() });

        AssertPageWasWritten("LoginForm");
        Assert.That(written.Select(Path.GetFileName),
            Is.EquivalentTo(new[] { "LoginForm.html", "LoginForm.css" }));
    }

    [Test]
    public void Emit_WritesIntoTheDirectoryItWasHanded_AndNeverComputesOne()
    {
        // The IDE uses bin\Debug and the CLI bin\Debug\<tfm>. Reusing the JS emitter's own output
        // directory is what makes them agree; computing a third path here is the bug this prevents.
        var nested = Path.Combine(_dir, "bin", "Debug", "net8.0");
        Directory.CreateDirectory(nested);

        FormAssetEmitter.Emit(nested, "Site.js", new[] { LoginForm() });

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(nested, "LoginForm.html")), Is.True);
            Assert.That(Directory.GetFiles(_dir, "*.html"), Is.Empty,
                "nothing may be written beside the sources");
        });
    }

    [Test]
    public void Emit_AlwaysOverwritesAFormPage()
    {
        // Form pages are generated files, not a harness: every form needs a starting point on every
        // build. index.html and package.json remain the only never-overwrite outputs.
        File.WriteAllText(Path.Combine(_dir, "LoginForm.html"), "stale");

        FormAssetEmitter.Emit(_dir, "Site.js", new[] { LoginForm() });

        Assert.That(File.ReadAllText(Path.Combine(_dir, "LoginForm.html")), Does.Not.Contain("stale"));
    }

    [Test]
    public void Emit_DoesNotTouchIndexHtmlOrPackageJson()
    {
        File.WriteAllText(Path.Combine(_dir, "index.html"), "<html>the user's own harness</html>");
        File.WriteAllText(Path.Combine(_dir, "package.json"), "{ \"name\": \"mine\" }");

        FormAssetEmitter.Emit(_dir, "Site.js", new[] { LoginForm() });

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(_dir, "index.html")),
                Is.EqualTo("<html>the user's own harness</html>"), "index.html must be byte-unchanged");
            Assert.That(File.ReadAllText(Path.Combine(_dir, "package.json")),
                Is.EqualTo("{ \"name\": \"mine\" }"));
        });
    }

    [Test]
    public void Emit_SkipsAWinFormsDocument()
    {
        // A .blform is a desktop window; it has no page. A mixed project is normal.
        var desktop = new FormDocument { Target = FormTarget.WinForms, Name = "MainForm" };

        var written = FormAssetEmitter.Emit(_dir, "Site.js", new[] { desktop });

        Assert.That(written, Is.Empty);
        Assert.That(Directory.GetFiles(_dir), Is.Empty);
    }

    // ==================================================================
    // The markup
    // ==================================================================

    [Test]
    public void Html_CarriesStableIds_AndNamesItsFormOnTheBody()
    {
        var html = FormAssetEmitter.Html(LoginForm(), "Site.js");

        Assert.Multiple(() =>
        {
            // The ids ARE the document's control ids — that is what lets the generated
            // InitializeComponent find each element with getElementById.
            Assert.That(html, Does.Contain("""id="lblUser" """.TrimEnd()));
            Assert.That(html, Does.Contain("""id="txtUser" """.TrimEnd()));
            Assert.That(html, Does.Contain("""id="btnLogin" """.TrimEnd()));
            Assert.That(html, Does.Contain("""<body data-form="LoginForm">"""),
                "Main() dispatches on this attribute");
            Assert.That(html, Does.Contain("""<script type="module" src="Site.js">"""));
            Assert.That(html, Does.Contain("""<link rel="stylesheet" href="LoginForm.css">"""));
        });
    }

    [Test]
    public void Html_UsesTheCatalogsTagAndInputType()
    {
        var html = FormAssetEmitter.Html(LoginForm(), "Site.js");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<label id=\"lblUser\""));
            Assert.That(html, Does.Contain("<input id=\"txtUser\"").And.Contain("type=\"text\""));
            Assert.That(html, Does.Contain("<button id=\"btnLogin\""));
        });
    }

    [Test]
    public void Html_PutsTextInTheRightPlaceForEachTag()
    {
        var html = FormAssetEmitter.Html(LoginForm(), "Site.js");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain(">Sign in</button>"), "a button carries its text as content");
            Assert.That(html, Does.Contain(">User</label>"));
            Assert.That(html, Does.Not.Contain("</input>"), "input is a void element");
        });
    }

    [Test]
    public void Html_PassesTheLiteralThroughUntouched()
    {
        // The runat="server" inversion (D9): it is markup the user wrote to BE markup.
        var html = FormAssetEmitter.Html(LoginForm(), "Site.js");

        Assert.That(html, Does.Contain("""<p class="hint">Use your work account.</p>"""),
            "the literal must not be escaped — escaping it would render the tags as text");
    }

    [Test]
    public void Html_EscapesUserTextButNotTheLiteral()
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "Esc" };
        var label = new FormControl { Kind = "Label", Id = "lbl", TabIndex = 0 };
        label.Properties["Text"] = "a < b & c";
        form.Controls.Add(label);

        var html = FormAssetEmitter.Html(form, "Site.js");

        Assert.That(html, Does.Contain("a &lt; b &amp; c"),
            "user text is text — an unescaped '<' would silently start a tag");
    }

    [Test]
    public void Html_EscapesAQuoteInAnAttributeValue()
    {
        // An unescaped quote closes the attribute early and makes the page unparseable.
        var form = new FormDocument { Target = FormTarget.Web, Name = "Q" };
        var box = new FormControl { Kind = "TextBox", Id = "txt", TabIndex = 0 };
        box.Properties["Text"] = """say "hi" """.TrimEnd();
        form.Controls.Add(box);

        var html = FormAssetEmitter.Html(form, "Site.js");

        Assert.That(html, Does.Contain("&quot;hi&quot;"));
    }

    [Test]
    public void Html_LeavesAVisibleCommentForAControlWithNoWebCatalogRow()
    {
        // Silently absent from the page would be indistinguishable from a layout bug.
        var form = new FormDocument { Target = FormTarget.Web, Name = "Gap" };
        form.Controls.Add(new FormControl { Kind = "Mystery", Id = "odd", TabIndex = 0 });

        var html = FormAssetEmitter.Html(form, "Site.js");

        Assert.That(html, Does.Contain("no web catalog row"));
    }

    // ==================================================================
    // The stylesheet
    // ==================================================================

    [Test]
    public void Css_TurnsTheDocumentsTrackListIntoRealGridTemplates()
    {
        // The document stores "120px,1fr" because it is one XML attribute; CSS wants spaces.
        var css = FormAssetEmitter.Css(LoginForm());

        Assert.Multiple(() =>
        {
            Assert.That(css, Does.Contain("display: grid;"));
            Assert.That(css, Does.Contain("grid-template-columns: 120px 1fr;"));
            Assert.That(css, Does.Contain("grid-template-rows: auto auto;"));
            Assert.That(css, Does.Contain("gap: 8px;"));
        });
    }

    [Test]
    public void Css_ConvertsZeroBasedCellsToOneBasedGridLines()
    {
        // ⚠ CSS grid lines are 1-based; the document's Col/Row are 0-based. Off by one puts every
        // control one cell down and to the right — which looks like a layout bug and is an indexing
        // one.
        var css = FormAssetEmitter.Css(LoginForm());

        Assert.Multiple(() =>
        {
            Assert.That(css, Does.Contain("#lblUser { grid-column: 1; grid-row: 1; }"));
            Assert.That(css, Does.Contain("#btnLogin { grid-column: 2; grid-row: 2; }"));
        });
    }

    [Test]
    public void Css_EmitsFlexForAFlowLayout()
    {
        var form = LoginForm();
        form.Layout = new FormLayout { Kind = FormLayoutKind.Flow, Dir = "Vertical", Gap = "4px" };

        var css = FormAssetEmitter.Css(form);

        Assert.Multiple(() =>
        {
            Assert.That(css, Does.Contain("display: flex;"));
            Assert.That(css, Does.Contain("flex-direction: column;"));
            Assert.That(css, Does.Not.Contain("grid-column"), "a flow layout has no grid cells");
        });
    }

    [Test]
    public void Css_HidesAnInvisibleControlRatherThanOmittingTheElement()
    {
        // The element must still EXIST for getElementById to find it, or the generated
        // InitializeComponent fails at run time on a control the user merely hid.
        var form = LoginForm();
        form.FindById("btnLogin")!.Properties["Visible"] = "false";

        Assert.Multiple(() =>
        {
            Assert.That(FormAssetEmitter.Css(form), Does.Contain("display: none"));
            Assert.That(FormAssetEmitter.Html(form, "Site.js"), Does.Contain("""id="btnLogin" """.TrimEnd()));
        });
    }

    // ==================================================================
    // The Main() dispatch (D7)
    // ==================================================================

    [Test]
    public void DispatchSource_ReadsTheBodyAttributeInTwoSteps()
    {
        // ⛔⛔ Measured: `Dim s As String = doc.body.getAttribute("data-form")` fails with "Cannot
        // assign value of type 'Object' to variable of type 'String'" — chained access through a
        // declared Property loses the declared type, even though both members are declared.
        var source = FormAssetEmitter.DispatchSource(new[] { "LoginForm", "SignupForm" });

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Dim b As Element = doc.body"));
            Assert.That(source, Does.Contain("Dim formName As String = b.getAttribute(\"data-form\")"));
            Assert.That(source, Does.Not.Contain("doc.body.getAttribute"),
                "the chained form does not type — this is the whole reason for the two-step shape");
        });
    }

    [Test]
    public void ASelectsItems_BecomeOptionChildren()
    {
        // ⛔⛔ The WinForms side emits Items.Add(...) per entry. Without the matching <option>s a
        // ComboBox rendered as an EMPTY dropdown on the web and a populated one on the desktop,
        // from the SAME document — a designer/runtime divergence across targets.
        var form = new FormDocument { Target = FormTarget.Web, Name = "F" };
        var combo = new FormControl { Kind = "ComboBox", Id = "cmb", TabIndex = 0 };
        combo.Properties["Items"] = "Alpha, Beta, Gamma";
        combo.Properties["Text"] = "Pick one";
        form.Controls.Add(combo);

        var html = FormAssetEmitter.Html(form, "App.js");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<option>Alpha</option>"));
            Assert.That(html, Does.Contain("<option>Beta</option>"));
            Assert.That(html, Does.Contain("<option>Gamma</option>"));
            // ⛔ A <select> takes <option> children and nothing else. Text was being written as a
            // bare text node inside it, which browsers drop or render as stray text.
            Assert.That(html, Does.Not.Contain(">Pick one<"));
            Assert.That(html, Does.Contain("""title="Pick one" """.TrimEnd()),
                "a select's Text labels it rather than filling it");
        });
    }

    [Test]
    public void ASelectsSelectedIndex_MarksThatOption()
    {
        // ⛔ The same cross-target divergence one property over. WinForms emits
        // `cmb.SelectedIndex = 1`; the page has no such property, so without marking the option the
        // desktop opened on Beta and the web opened on Alpha — from the same document.
        var form = new FormDocument { Target = FormTarget.Web, Name = "F" };
        var combo = new FormControl { Kind = "ComboBox", Id = "cmb", TabIndex = 0 };
        combo.Properties["Items"] = "Alpha, Beta, Gamma";
        combo.Properties["SelectedIndex"] = "1";
        form.Controls.Add(combo);

        var html = FormAssetEmitter.Html(form, "App.js");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<option>Alpha</option>"));
            Assert.That(html, Does.Contain("<option selected>Beta</option>"));
            Assert.That(html, Does.Contain("<option>Gamma</option>"));
        });
    }

    [Test]
    public void ASelectsSelectedIndexOutOfRange_MarksNothing()
    {
        // -1 is the catalog default (nothing chosen) and anything past the end is a document the
        // user can produce by shortening Items. Neither may mark an arbitrary option.
        var form = new FormDocument { Target = FormTarget.Web, Name = "F" };
        var combo = new FormControl { Kind = "ComboBox", Id = "cmb", TabIndex = 0 };
        combo.Properties["Items"] = "Alpha, Beta";
        combo.Properties["SelectedIndex"] = "7";
        form.Controls.Add(combo);

        Assert.That(FormAssetEmitter.Html(form, "App.js"), Does.Not.Contain("selected"));
    }

    /// <summary>
    /// ⛔ Both targets must agree on which SelectedIndex values are valid. WinForms judges these
    /// Degraded (<c>FormPropertyDef.TryParseInt</c>: ASCII space only, culture-free) and emits no
    /// assignment; a whitespace-tolerant <c>int.TryParse</c> on the web marked Beta anyway, so the
    /// same document opened on different entries per target.
    /// </summary>
    [TestCase("1\r\n")]
    [TestCase("\t1")]
    [TestCase("1\n")]
    public void ASelectsSelectedIndex_ThatWinFormsJudgesDegraded_MarksNothing(string raw)
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "F" };
        var combo = new FormControl { Kind = "ComboBox", Id = "cmb", TabIndex = 0 };
        combo.Properties["Items"] = "Alpha, Beta";
        combo.Properties["SelectedIndex"] = raw;
        form.Controls.Add(combo);

        Assert.Multiple(() =>
        {
            Assert.That(FormControlCatalog.Find("ComboBox")!.Property("SelectedIndex")!.Accepts(raw), Is.False,
                "precondition: the catalog calls this value Degraded");
            Assert.That(FormAssetEmitter.Html(form, "App.js"), Does.Not.Contain("selected"));
        });
    }

    private static string NumericHtml(string property, string raw)
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "F" };
        var numeric = new FormControl { Kind = "NumericUpDown", Id = "num", TabIndex = 0 };
        numeric.Properties[property] = raw;
        form.Controls.Add(numeric);
        return FormAssetEmitter.Html(form, "App.js");
    }

    /// <summary>
    /// ⛔ An Int row's HTML attribute (min/max/value/step) is re-emitted from the PARSED number, the
    /// same way WinForms emits it — never the document's text, whatever the parser tolerated around it.
    /// </summary>
    [Test]
    public void AnIntHtmlAttribute_IsEmittedFromTheParsedNumber()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NumericHtml("Value", " 5 "), Does.Contain(" value=\"5\""));
            Assert.That(NumericHtml("Value", "+007"), Does.Contain(" value=\"7\""));
        });
    }

    /// <summary>
    /// ⛔ WinForms emits NOTHING for a Degraded Int (the catalog refuses it); the page must not carry
    /// it either, or the same document means a number on one target and nothing on the other.
    /// </summary>
    [TestCase("abc", TestName = "{m}(abc)")]
    [TestCase("5\r\n", TestName = "{m}(trailing CRLF)")]
    [TestCase("<MINUS>5", TestName = "{m}(U+2212)")]
    public void ADegradedIntHtmlAttribute_IsNotEmitted(string marked)
    {
        var raw = marked.Replace("<MINUS>", UnicodeMinusCulture.Minus);

        Assert.Multiple(() =>
        {
            Assert.That(FormControlCatalog.Find("NumericUpDown")!.Property("Value")!.Accepts(raw), Is.False,
                "precondition: the catalog calls this value Degraded");
            Assert.That(NumericHtml("Value", raw), Does.Not.Contain(" value="));
        });
    }

    [Test]
    [SetCulture("sv-SE")]
    public void ANegativeIntHtmlAttribute_UnderAUnicodeMinusCulture_UsesAnAsciiHyphen()
    {
        UnicodeMinusCulture.Require();

        var html = NumericHtml("Minimum", "-5");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain(" min=\"-5\""));
            Assert.That(html, Does.Not.Contain(UnicodeMinusCulture.Minus));
        });
    }

    [Test]
    public void ASelectsSelectedIndex_WithAsciiSpaces_StillMarksThatOption()
    {
        // Space is what a person types beside a number; TryParseInt accepts it, so both targets do.
        var form = new FormDocument { Target = FormTarget.Web, Name = "F" };
        var combo = new FormControl { Kind = "ComboBox", Id = "cmb", TabIndex = 0 };
        combo.Properties["Items"] = "Alpha, Beta";
        combo.Properties["SelectedIndex"] = " 1 ";
        form.Controls.Add(combo);

        Assert.That(FormAssetEmitter.Html(form, "App.js"), Does.Contain("<option selected>Beta</option>"));
    }

    [Test]
    public void ASelectWithNoItems_EmitsNoOptions()
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "F" };
        form.Controls.Add(new FormControl { Kind = "ListBox", Id = "lst", TabIndex = 0 });

        Assert.That(FormAssetEmitter.Html(form, "App.js"), Does.Not.Contain("<option"));
    }

    [Test]
    public void DispatchSource_ConstructsTheForm_WithoutCallingInitializeComponentAgain()
    {
        // ⛔⛔ The scaffolded `Public Sub New()` already calls InitializeComponent. Calling it
        // again here ran the entire init body TWICE — so every addEventListener registered its
        // handler twice and a single click fired it twice. Nothing about the page looked wrong.
        var source = FormAssetEmitter.DispatchSource(new[] { "LoginForm" });

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Dim f As New LoginForm()"));
            Assert.That(source, Does.Not.Contain("InitializeComponent"),
                "the constructor does it; a second call double-registers every handler");
        });
    }

    [Test]
    public void DispatchSource_BranchesOnEveryForm_UnderOneTopLevelName()
    {
        // One generated top-level name per project, fixed spelling, so --check can collision-check
        // it once. Per-control fields are class members and cannot collide across forms.
        var source = FormAssetEmitter.DispatchSource(new[] { "LoginForm", "SignupForm" });

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain($"Sub {FormAssetEmitter.DispatchSubName}()"));
            Assert.That(source, Does.Contain("""If formName = "LoginForm" Then"""));
            Assert.That(source, Does.Contain("""ElseIf formName = "SignupForm" Then"""));
            Assert.That(source, Does.Contain("End If"));
            // ⛔ Counted as two separate properties, not as occurrences of "Sub ". The closing
            // keyword is "End Sub" at the end of its line, with no trailing space, so a single
            // count of "Sub " never sees it — an assertion of 2 fails against output that is
            // exactly right, and one of 1 would pass just as well against output carrying a second
            // declaration and no terminator.
            Assert.That(source.Split("Sub ").Length - 1, Is.EqualTo(1),
                "exactly one Sub DECLARATION — no second top-level name to collide across forms");
            Assert.That(source.Split("End Sub").Length - 1, Is.EqualTo(1),
                "and it is terminated exactly once");
        });
    }

    [Test]
    public void DispatchSource_IsWellFormed_WithNoForms()
    {
        var source = FormAssetEmitter.DispatchSource(Array.Empty<string>());

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain($"Sub {FormAssetEmitter.DispatchSubName}()"));
            Assert.That(source, Does.Contain("End Sub"));
            Assert.That(source, Does.Not.Contain("End If"),
                "an If that was never opened must not be closed");
        });
    }
}
